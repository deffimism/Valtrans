# Lite 호스트 빌드 / Build the Lite host

Windows x64 + Python 3.12에서 별도 환경을 사용합니다. 사용자 Python이나 설치된 번역 모델을 수정하지 않습니다.

```powershell
python -m venv .lite-build/venv
./.lite-build/venv/Scripts/python.exe -m pip install -r Lite/requirements-build.txt
./scripts/Build-LiteHost.ps1
```

The script runs tokenizer/cache unit tests, builds a pipe-compatible executable, and checks status/shutdown on that actual executable. The [PyInstaller console option](https://pyinstaller.org/en/stable/usage.html#windows-and-macos-specific-options) preserves standard input/output; the app launches the helper hidden. Do not use `--noconsole`, which removes the streams this protocol requires.

Output: `.lite-build/dist/ValtransLiteHost.exe` and `build-manifest.json` with source/executable SHA256 and dependency versions. Building does **not** automatically overwrite `Assets/ValtransLiteHost.exe`.

Before promoting the executable, test real translations with both the existing shared SentencePiece packages and separate `source.spm`/`target.spm` packages in isolated model directories. Recheck source SHA before copying the executable into Assets and change `ValtransLiteService.HostFileName` when its embedded contents change. Rebuild the application and verify extraction of the new host.

The cache holds at most four language pairs, needed for alternating KO↔EN and JP↔EN translation. It releases idle models after ten minutes, and the app's memory-release command clears them immediately. Peak resident memory must include the onefile child process running CTranslate2, not only the small parent bootloader. The four-pair comparison measured roughly 616 MiB with existing Argos models and 705 MiB with the experimental larger KO→EN model (sum of per-process peaks, one test PC; not a hardware guarantee).
