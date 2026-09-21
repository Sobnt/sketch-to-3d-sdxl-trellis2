# Trellis Bridge per Unity

Questo bridge Flask va eseguito sul PC dove hai installato Trellis 2 dentro ComfyUI.

## 1. Avvia ComfyUI Trellis

```bat
conda activate comfy-trellis2
cd /d C:\AI\TrellisTest\ComfyUI
python main.py
```

ComfyUI deve restare raggiungibile localmente su `http://127.0.0.1:8188`.

## 2. Esporta il workflow API

In ComfyUI apri il workflow Trellis che hai testato, abilita le opzioni developer e salva il workflow in formato API.

Salvalo ad esempio in:

```text
C:\AI\TrellisTest\trellis_workflow_api.json
```

Il workflow deve contenere un nodo `LoadImage` collegato al nodo `TRELLIS.2 Get Conditioning`.

## 3. Avvia il bridge

Da un secondo terminale:

```bat
conda activate comfy-trellis2
python C:\percorso\al\progetto\ego2\TrellisBridge\trellis_bridge_server.py --host 0.0.0.0 --port 5055 --workflow C:\AI\TrellisTest\trellis_workflow_api.json
```

Se il bridge non trova automaticamente il nodo immagine, avvialo indicando l'id del nodo `LoadImage`:

```bat
python C:\percorso\al\progetto\ego2\TrellisBridge\trellis_bridge_server.py --host 0.0.0.0 --port 5055 --workflow C:\AI\TrellisTest\trellis_workflow_api.json --image-node-id 12
```

## 4. URL da usare in Unity

Nel tuo caso il PC Trellis ha IP VPN:

```text
172.16.101.15
```

La seconda scena Unity usa gia' come default:

```text
http://172.16.101.15:5055
```

Puoi testare dal browser del PC Unity:

```text
http://172.16.101.15:5055/health
```

La risposta deve contenere `"status": "ok"`.
