# Pipeline sketch-to-3D

Pipeline per trasformare uno sketch in un'immagine 2D e, successivamente, in un asset 3D in formato GLB. La generazione 2D usa SDXL e ControlNet Scribble in ComfyUI; la generazione 3D usa TRELLIS.2 in un secondo workflow ComfyUI.

## Stato della repository

Questa cartella contiene i due server Flask, i workflow ComfyUI e immagini di esempio. I servizi possono essere provati tramite le rispettive API; il server 2D include anche una pagina web di prova.

Per installare il progetto su un altro computer, seguire [REPRODUCIBILITY.md](REPRODUCIBILITY.md). La guida distingue il software presente qui dai modelli e nodi esterni che devono ancora essere installati.

L'esecuzione descritta nel progetto usa due ambienti: il workflow 2D sul server universitario e il workflow TRELLIS.2 su un computer locale. `127.0.0.1` indica sempre la macchina su cui gira il processo che lo usa.

## Contenuto

| Percorso | Contenuto |
| --- | --- |
| `SDXL/app_sdxl.py` | API Flask per preprocessing dello sketch e generazione 2D; include una pagina web di prova. |
| `Trellis2/trellis_bridge_server.py` | Bridge Flask per avviare TRELLIS.2, seguire i job e scaricare il GLB. |
| `ComfyUI_Workflows/Workflow_SDXL/sdxl_scribble_workflow.json` | Rappresentazione API del workflow SDXL; lo script 2D costruisce comunque il workflow direttamente nel codice. |
| `ComfyUI_Workflows/Workflow_Trellis2/trellis_workflow_api.json` | Workflow API usato dal bridge 3D. |
| `Immagini_esempio/` | Sketch e risultati raccolti durante le prove; i nomi dei file non rappresentano una classificazione automatica dei risultati. |
| `requirements-server.txt` | Dipendenze Python dirette dei due server Flask; non include le dipendenze di ComfyUI. |
| `scripts/check_setup.py` | Controllo preliminare dei nodi e dei checkpoint visibili a ComfyUI. |
| `REPRODUCIBILITY.md` | Guida per preparare i due ambienti ed eseguire un test completo. |

## Requisiti

- Python e accesso a un'installazione funzionante di ComfyUI su ciascuna macchina che esegue un workflow.
- Pacchetti Python per i server: `flask`, `requests`, `ollama`, `opencv-python`, `numpy`. Il pacchetto `ollama` va installato anche quando l'arricchimento del prompt e disattivato, perche viene importato all'avvio di `app_sdxl.py`.
- Per il workflow 2D: checkpoint `sdxl-base-1.0.safetensors`, modello `sdxl_scribble_controlnet.safetensors`, nodo `ETN_LoadImageBase64` e nodi di preprocessing che includano `AIO_Preprocessor` / `ScribblePreprocessor`.
- Per il workflow 3D: modelli TRELLIS.2 e nodi ComfyUI che forniscono le classi `LoadTrellis2Models`, `Trellis2RemoveBackground`, `Trellis2GetConditioning`, `Trellis2ImageToShape`, `Trellis2ShapeToTexturedMesh`, `Trellis2ProcessMesh`, `Trellis2RasterizePBR` e `Trellis2ExportTrimesh`. Il workflow include anche `Preview3D`.
- Hardware capace di eseguire i modelli scelti. Questa repository non contiene checkpoint, pesi o un ambiente ComfyUI preconfigurato. Seguire le istruzioni e le licenze dei rispettivi progetti per ottenere modelli e nodi.

Le versioni esatte di Python, ComfyUI, nodi personalizzati e driver usate negli esperimenti non sono ancora registrate in questa repository. La compatibilita con versioni diverse deve essere verificata nel proprio ambiente.

## Preparazione Python

Dalla cartella principale della repository, in PowerShell:

```powershell
py -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install -r requirements-server.txt
```

Per usare Ollama nella modalita di prompt augmentation, avviare anche il servizio Ollama e rendere disponibile il modello `llama3.1`. Se il servizio non risponde, lo script 2D ripiega sul template testuale fisso. Si puo evitare la chiamata al modello passando `"magic_enrich": false` nella richiesta.

## Generazione 2D

1. Installare in ComfyUI i modelli e i nodi richiesti dal workflow SDXL. Verificare che i nomi dei file dei modelli corrispondano a quelli usati in `SDXL/app_sdxl.py`.
2. Avviare ComfyUI sulla **stessa macchina** del server Flask 2D, raggiungibile all'indirizzo `http://127.0.0.1:8188`. Lo script usa questo indirizzo come valore fisso di `COMFYUI_URL`.
3. Dalla cartella principale, avviare:

```powershell
python .\SDXL\app_sdxl.py
```

4. Aprire `http://127.0.0.1:5001` sulla macchina del server per usare la pagina di prova. Caricare uno sketch, inserire il prompt, scegliere i parametri e generare l'immagine.

L'endpoint `POST /generate` accetta JSON con `image` (immagine codificata in base64), `prompt` e, opzionalmente, `magic_enrich`, `strength`, `start_percent`, `end_percent`, `cfg` e `steps`. La risposta riuscita contiene `status: "success"`, `image` (PNG in base64) e `prompt_usato`. Lo script centra lo sketch su una tela bianca 1024x1024, costruisce il workflow API, interroga ComfyUI e restituisce l'immagine generata. `GET /health` controlla il servizio Flask, non lo stato di ComfyUI.

## Generazione 3D

1. Installare e verificare TRELLIS.2 e i relativi nodi nella propria istanza locale di ComfyUI. Verificare che il workflow `trellis_workflow_api.json` venga accettato da tale installazione.
2. Avviare ComfyUI sulla **stessa macchina** del bridge, raggiungibile all'indirizzo `http://127.0.0.1:8188`.
3. Dalla cartella principale della repository, avviare il bridge indicando il percorso del workflow API:

```powershell
python .\Trellis2\trellis_bridge_server.py --workflow .\ComfyUI_Workflows\Workflow_Trellis2\trellis_workflow_api.json
```

Il bridge ascolta per impostazione predefinita sulla porta `5055`. Per la prova locale, l'indirizzo e `http://127.0.0.1:5055`. Il comando deve ricevere `--workflow` perche il file JSON non si trova nella directory di lavoro predefinita dello script.

La sequenza API e la seguente:

1. `POST /generate-3d` con JSON contenente `image` (immagine 2D in base64) e, facoltativamente, `file_name`. Il server risponde con `job_id` e stato `queued`.
2. `GET /jobs/<job_id>` per controllare l'avanzamento. Attendere `status: "success"` oppure leggere il messaggio in caso di `status: "error"`.
3. `GET /download/<job_id>` per scaricare il file GLB una volta completato il job.

Il workflow contiene la rimozione dello sfondo, il condizionamento dall'immagine, la generazione della forma e della mesh texturizzata, il trattamento della mesh, la rasterizzazione PBR e l'esportazione GLB. Nel JSON incluso sono impostati `target_face_count: 5000` e `texture_size: 1024`; modificarli nel workflow API se si desidera un'altra configurazione. Il JSON API contiene valori di seed espliciti; la randomizzazione osservata nell'interfaccia grafica non e documentata dal file API.

## Prima della pubblicazione

- Verificare che immagini di esempio e altri materiali inclusi possano essere redistribuiti; indicare la provenienza degli esempi non originali.
- Non aggiungere credenziali, indirizzi privati, ambienti virtuali, checkpoint, output generati o file di backup del progetto.
- Aggiungere un file `LICENSE` solo dopo aver scelto le condizioni di distribuzione del codice; le licenze dei modelli e dei nodi esterni restano separate.
- Eseguire almeno una prova completa dei due servizi nell'ambiente di destinazione e annotare versioni e nodi effettivamente usati.
