import contextlib
import json
import logging
import os
from pathlib import Path
import sys

root = Path(__file__).resolve().parent
for name, path in {
    'XDG_DATA_HOME': (root.parent / 'translator-v1' / 'data') if (root.parent / 'translator-v1' / 'data').exists() else root / 'data', 'XDG_CONFIG_HOME': root / 'config',
    'XDG_CACHE_HOME': root / 'cache', 'HF_HOME': root / 'cache' / 'huggingface',
}.items():
    path.mkdir(parents=True, exist_ok=True)
    os.environ[name] = str(path)
os.environ['ARGOS_DEVICE_TYPE'] = 'cpu'
os.environ['ARGOS_INTRA_THREADS'] = '2'
os.environ['ARGOS_INTER_THREADS'] = '1'
os.environ['PYTHONUTF8'] = '1'
sys.stdin.reconfigure(encoding='utf-8')
sys.stdout.reconfigure(encoding='utf-8')
logging.disable(logging.CRITICAL)
protocol = sys.stdout

def emit(**value):
    protocol.write(json.dumps(value, ensure_ascii=True) + '\n')
    protocol.flush()

# Library diagnostics may contain message text; never persist them.
with open(os.devnull, 'w') as quiet, contextlib.redirect_stdout(quiet), contextlib.redirect_stderr(quiet):
    import argostranslate.package as package
    import argostranslate.translate as translate
    from langdetect import detect, DetectorFactory
    DetectorFactory.seed = 0
    codes = tuple(os.environ['CHATTER_LANGUAGES'].split(','))
    # Deterministic detector; explicit script/channel hints arrive from the app.
    emit(ready=True)
    available = None

    def ensure_pair(source, target):
        global available
        if any(p.from_code == source and p.to_code == target for p in package.get_installed_packages()):
            return
        emit(status=f'Downloading the {source} → {target} translation model. First use may take several minutes…')
        if available is None:
            package.update_package_index()
            available = package.get_available_packages()
        model = next((p for p in available if p.from_code == source and p.to_code == target), None)
        if model is None:
            raise ValueError('This language pair is not available locally.')
        model.install()

    for line in sys.stdin:
        try:
            request = json.loads(line)
            text, source, target = request['text'], request['source'], request['target']
            if request.get('preload'):
                if source not in codes or target not in codes:
                    raise ValueError('Unsupported language pair.')
                for a, b in ((source, 'en'), ('en', target)):
                    if a != b:
                        ensure_pair(a, b)
                emit(translatedText='Models are ready.')
                continue
            if not any(c.isalpha() for c in text):
                emit(translatedText=text)
                continue
            if source == 'auto':
                try:
                    source = detect(text)
                except Exception:
                    emit(translatedText=text)
                    continue
                source = {'zh-cn': 'zh', 'zh-tw': 'zh', 'iw': 'he'}.get(source, source)
                # Short informal Cyrillic is frequently misclassified by statistical detectors.
                if any('\u0400' <= c <= '\u04ff' for c in text) and source not in ('ru', 'uk'):
                    source = 'uk' if any(c in text.lower() for c in 'іїєґ') else 'ru'
            if source not in codes and request['source'] == 'auto':
                emit(translatedText=text)
                continue
            if source not in codes or target not in codes or len(text.encode('utf-8')) > 8192:
                raise ValueError('Unsupported translation request.')
            if source == target or not any(c.isalpha() for c in text):
                result = text
            else:
                # Pivot through English so only the needed models are installed.
                for a, b in ((source, 'en'), ('en', target)):
                    if a != b:
                        ensure_pair(a, b)
                emit(status='Translating…')
                result = translate.translate(text, source, target)
            emit(translatedText=result)
        except Exception:
            emit(error='Local translation failed. Check your connection for first-use model downloads, then retry.')
