"""PP-OCRv5 fast OCR worker. Same stdin/stdout JSON protocol as paddle_host.py (VL).

Uses paddleocr when available. stdout is JSON-lines only.
"""
import argparse
import base64
import contextlib
import io
import json
import os
import sys
import time

os.environ["PYTHONIOENCODING"] = "utf-8"
sys.stdin.reconfigure(encoding="utf-8")
sys.stdout.reconfigure(encoding="utf-8")


def send(value):
    print(json.dumps(value, ensure_ascii=False), flush=True)


def build_ocr():
    from paddleocr import PaddleOCR

    # Game chat crops are upright screen pixels, not scanned/rotated documents.
    # Disable document dewarping and rotation models explicitly: their defaults
    # add inference stages and can distort small anti-aliased chat glyphs.
    kwargs = {
        "use_doc_orientation_classify": False,
        "use_doc_unwarping": False,
        "use_textline_orientation": False,
    }
    try:
        return PaddleOCR(ocr_version="PP-OCRv5", lang="ch", **kwargs)
    except TypeError:
        return PaddleOCR(lang="ch", **kwargs)


def _append_line(lines, scores, text, score):
    text = (text or "").strip()
    if not text:
        return
    lines.append(text)
    try:
        scores.append(float(score))
    except (TypeError, ValueError):
        scores.append(0.0)


def recognize(ocr, png: bytes):
    from PIL import Image
    import numpy as np

    with Image.open(io.BytesIO(png)) as source:
        if source.width < 20 or source.height < 20 or source.width * source.height > 4_000_000:
            raise ValueError("OCR 영역은 채팅 부분만 작게 지정하세요 (최대 400만 픽셀).")
        image = np.array(source.convert("RGB"))
    try:
        result = ocr.predict(image)
    except AttributeError:
        result = ocr.ocr(image)
    lines = []
    scores = []
    for block in result or []:
        if isinstance(block, dict):
            rec_texts = block.get("rec_texts") or []
            rec_scores = block.get("rec_scores") or []
            if rec_scores and len(rec_scores) == len(rec_texts):
                for text, score in zip(rec_texts, rec_scores):
                    _append_line(lines, scores, text, score)
            else:
                for text in rec_texts:
                    _append_line(lines, scores, text, 0.0)
            continue
        for item in block or []:
            if not item or len(item) < 2:
                continue
            _append_line(lines, scores, item[1][0], item[1][1])
    confidence = round(sum(scores) / len(scores), 4) if scores else 0.0
    return "\n".join(lines), confidence


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--runtime", required=True)
    args = parser.parse_args()
    from chat_ocr import ChatRecognizer
    with contextlib.redirect_stdout(sys.stderr):
        chat = ChatRecognizer()
        chat.prepare(['EN', 'JP', 'KO'])
    ocr = None
    send({"ready": True, "engine": "PP-OCRv5", "runtime": args.runtime})
    while True:
        line = sys.stdin.readline(12_000_001)
        if not line:
            return
        if len(line) > 12_000_000 or not line.endswith("\n"):
            raise ValueError("OCR 요청 크기가 너무 큽니다.")
        request = json.loads(line)
        if request.get("command") == "stop":
            return
        request_id = request.get("id")
        try:
            start = time.perf_counter()
            png = base64.b64decode(request["png"], validate=True)
            from PIL import Image
            with Image.open(io.BytesIO(png)) as original:
                if original.width < 20 or original.height < 20 or original.width * original.height > 4_000_000:
                    raise ValueError('Invalid OCR image dimensions')
                frame = original.convert('RGB')
            languages = request.get('languages') or ['EN', 'JP', 'KO']
            languages = [language for language in languages if language in ('EN', 'JP', 'KO', 'ZH')]
            with contextlib.redirect_stdout(sys.stderr):
                rows = chat.recognize(frame, languages) if request.get('game') == 'VALORANT' else None
                if rows is None:
                    if ocr is None:
                        ocr = build_ocr()
                    text, confidence = recognize(ocr, png)
                else:
                    text = '\n'.join(row['text'] for row in rows)
                    confidence = min((row['confidence'] for row in rows), default=0.0)
            send({
                "id": request_id,
                "text": text,
                "confidence": confidence,
                "lines": rows,
                "imageWidth": frame.width,
                "stage": 'chat-rows' if rows is not None else 'document-fallback',
                "finished": True,
                "milliseconds": round((time.perf_counter() - start) * 1000, 1)
            })
        except Exception as ex:
            send({"id": request_id, "error": str(ex)[:300]})


if __name__ == "__main__":
    try:
        main()
    except Exception as ex:
        send({"ready": False, "error": str(ex)[:300]})
        sys.exit(1)
