# Riprodurre la pipeline senza il client immersivo

Questa guida copre il codice pubblicato: API 2D, bridge 3D e workflow ComfyUI. Non include il client immersivo. Un'installazione pulita richiede due ambienti, uno per la generazione 2D e uno per TRELLIS.2; possono essere due computer distinti. Gli indirizzi `127.0.0.1` negli script indicano sempre la macchina su cui gira il rispettivo server Flask.

## 1. Prerequisiti comuni

Installare Python, Git e ComfyUI seguendo la [guida ufficiale](https://github.com/Comfy-Org/ComfyUI). Usare l'ambiente Python di ComfyUI per installare le dipendenze dei suoi nodi personalizzati; l'ambiente dei server Flask puo essere separato. Preparare driver e librerie GPU come richiesto dai modelli e dalle istruzioni dei nodi. La compatibilita con una specifica GPU non e stata certificata da questo repository.

Per entrambi i server Flask, dalla radice del repository:

```powershell
py -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install -r requirements-server.txt
```

`requirements-server.txt` contiene solo le dipendenze dirette dei server, **non** quelle di ComfyUI o di TRELLIS.2. Le versioni sperimentali originali non sono state archiviate: il file non e un lockfile e non garantisce ancora riproducibilita bit-per-bit.

## 2. Ambiente 2D

Installare nella sua istanza ComfyUI:

| Componente | Provenienza / collocazione | Controllo |
| --- | --- | --- |
| ComfyUI | [progetto ufficiale](https://github.com/Comfy-Org/ComfyUI) | API su `127.0.0.1:8188` |
| Nodo `ETN_LoadImageBase64` | [comfyui-tooling-nodes](https://github.com/Acly/comfyui-tooling-nodes) | presente in `/object_info` |
| `AIO_Preprocessor` e `ScribblePreprocessor` | [comfyui_controlnet_aux](https://github.com/Fannovel16/comfyui_controlnet_aux) | presenti/selezionabili in `/object_info` |
| Checkpoint SDXL base | [scheda ufficiale Stability AI](https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0) | installato in `ComfyUI/models/checkpoints/` con nome `sdxl-base-1.0.safetensors` |
| ControlNet Scribble per SDXL | **Origine del file usato da documentare** | installato in `ComfyUI/models/controlnet/` con nome `sdxl_scribble_controlnet.safetensors` |

Il nome del file ControlNet non identifica univocamente il modello originale: potrebbe essere stato rinominato. Prima di pubblicare istruzioni di download precise occorrono il link del checkpoint effettivamente usato e, idealmente, il suo SHA-256. Non sostituirlo alla cieca con un modello Scribble per SD 1.5.

Avviare ComfyUI sulla **stessa macchina** del server 2D e controllare:

```powershell
python .\scripts\check_setup.py 2d
python .\SDXL\app_sdxl.py
```

L'API Flask ascolta sulla porta 5001; la pagina di prova e `http://127.0.0.1:5001/`. Il server costruisce il workflow API nel codice. Il JSON `sdxl_scribble_workflow.json` e un esempio da eseguire manualmente in ComfyUI: usa `LoadImage`, mentre l'API Flask usa `ETN_LoadImageBase64`. Non modificare il JSON aspettandosi che cambi automaticamente il comportamento del server.

Ollama e `llama3.1` sono necessari solo per provare la prompt augmentation. Il pacchetto Python `ollama` e comunque importato all'avvio. Per una prova senza il servizio Ollama, inviare `"magic_enrich": false` in `POST /generate`; il server applichera il template fisso.

## 3. Ambiente 3D

Installare una seconda istanza funzionante di ComfyUI e i nodi TRELLIS.2 che espongono le classi elencate nel JSON API. Il [progetto ComfyUI-TRELLIS2](https://github.com/PozzettiAndrea/ComfyUI-TRELLIS2) e una possibile sorgente dei nodi; **verificare nella propria installazione** che nomi, input e pesi coincidano con il workflow pubblicato. Seguire le istruzioni del progetto dei nodi per Python, PyTorch, CUDA, estensioni compilate e download dei pesi. Non sono inclusi qui e la loro versione originaria va ancora registrata.

Avviare ComfyUI sulla stessa macchina del bridge. Poi eseguire:

```powershell
python .\scripts\check_setup.py 3d
python .\Trellis2\trellis_bridge_server.py --workflow .\ComfyUI_Workflows\Workflow_Trellis2\trellis_workflow_api.json --comfy-output-dir C:\percorso\alla\ComfyUI\output
```

Sostituire il percorso della cartella `output`. Il bridge tenta di ricavarlo se `--comfy-output-dir` manca, ma in una nuova installazione questa deduzione puo essere errata. Il bridge ascolta sulla porta 5055. Il JSON API include `LoadImage` e viene aggiornato dal bridge dopo l'upload dell'immagine; i valori `target_face_count: 5000` e `texture_size: 1024` sono quelli salvati nel workflow.

## 4. Prova completa

1. Verificare `GET /health` per entrambi i server. Questo prova Flask, non l'intera catena AI.
2. Inviare uno sketch in base64 a `POST http://<server-2d>:5001/generate`, con `prompt` e `magic_enrich: false`. Verificare che la risposta contenga `status: success` e `image`.
3. Inviare quell'immagine a `POST http://<bridge-3d>:5055/generate-3d` nel campo JSON `image`. Annotare il `job_id`.
4. Interrogare `GET /jobs/<job_id>` fino allo stato `success` o `error`.
5. Scaricare `GET /download/<job_id>` e verificare che il file sia un GLB apribile in un visualizzatore glTF. Una risposta positiva del preflight non sostituisce questa prova.

Per la sintassi esatta dei payload e dei campi di risposta, vedere il [README](README.md) e i due script server. Su macchine diverse, gli indirizzi pubblici/di rete dei server devono essere raggiungibili dal client; non esporre le API direttamente su Internet senza autenticazione e protezioni aggiuntive.

## 5. Informazioni ancora da acquisire dall'ambiente originale

- Commit/versione di ComfyUI, Python, PyTorch, CUDA e driver GPU sui due computer.
- Repository e commit dei nodi personalizzati effettivamente installati, soprattutto quelli TRELLIS.2.
- URL, licenza e SHA-256 dei checkpoint SDXL, ControlNet Scribble e dei pesi TRELLIS.2 effettivamente usati.
- Una prova 2D e una 3D eseguite da una nuova installazione, con log e tempi osservati.

Finche questi dati non sono registrati, la guida rende esplicite le dipendenze ma non dimostra una riproduzione identica dell'ambiente sperimentale.
