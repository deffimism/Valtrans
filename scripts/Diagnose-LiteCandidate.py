"""Compare official PyTorch and converted CT2 outputs before judging quality."""
import json
from pathlib import Path
import sys
sys.stdout.reconfigure(encoding='utf-8')

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'artifacts/lite-candidate-runtime'))
import torch
import ctranslate2
from transformers import MarianMTModel, MarianTokenizer
from safetensors import safe_open

torch.set_num_threads(4)
folder = ROOT / 'artifacts/lite-candidate-ko-en/original'
with safe_open(folder / 'model.safetensors', framework='pt', device='cpu') as weights:
    print([(key, weights.get_slice(key).get_shape()) for key in weights.keys() if 'embed_tokens' in key or 'shared' in key], flush=True)
tokenizer = MarianTokenizer.from_pretrained(folder, local_files_only=True)
model = MarianMTModel.from_pretrained(folder, local_files_only=True, use_safetensors=True).eval()
translator = ctranslate2.Translator(str(folder.parent / 'ct2-int8'), device='cpu', intra_threads=4)
for source in ('안녕하세요', '2, 4, 6 등은 짝수이다.', '잠깐 화장실 좀 다녀올게', '오늘 처음 이겼네 기분 좋다'):
    inputs = tokenizer(source, return_tensors='pt')
    tokens = tokenizer.convert_ids_to_tokens(inputs['input_ids'][0].tolist())
    with torch.inference_mode():
        result = model.generate(**inputs, max_new_tokens=100, num_beams=1)
    ct2 = translator.translate_batch([tokens], beam_size=1)[0].hypotheses[0]
    print(json.dumps(dict(source=source, spm=tokenizer.spm_source.encode(source, out_type=str),
        tokens=tokens, ids=inputs['input_ids'][0].tolist(), torch=tokenizer.decode(result[0], skip_special_tokens=True),
        ct2tokens=ct2, ct2=tokenizer.decode(tokenizer.convert_tokens_to_ids(ct2), skip_special_tokens=True)), ensure_ascii=False), flush=True)
