# Valtrans Lite Korean → English model

Based on the **Helsinki-NLP / OPUS-MT Tatoeba Challenge** Korean-to-English model
`opusTCv20210807-sepvoc_transformer-big_2022-07-28`.

- Original model: https://object.pouta.csc.fi/Tatoeba-MT-models/kor-eng/opusTCv20210807-sepvoc_transformer-big_2022-07-28.zip
- Model card: https://huggingface.co/Helsinki-NLP/opus-mt-tc-big-ko-en
- Original archive SHA256: `9132ed606b6964656520931d96a4a6afc7ddc7d55ca5438e1d403db0c05492ef`
- License: **Creative Commons Attribution 4.0 International**. The original `LICENSE` and model README are included.

Valtrans converted the original Marian weights to CTranslate2 INT8 format using
the original, separate source/target vocabularies and SentencePiece models.
This is a format conversion and quantization, not additional model training.
The authors do not endorse Valtrans. Translation errors remain possible,
especially slang, implied actors, and Korean → English → Japanese pivoting.

The Hugging Face converted weights are not used in this package; the original
Marian archive is the source of the weights and tokenizer files.
