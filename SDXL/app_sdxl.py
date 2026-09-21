from flask import Flask, request, jsonify, render_template_string
import requests
import base64
import time
import uuid
import random
import ollama
import cv2
import numpy as np


app = Flask(__name__)

COMFYUI_URL = "http://127.0.0.1:8188"

HTML_PAGE = """
<!DOCTYPE html>
<html lang="it">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>Sketch → 3D · SDXL</title>
<style>
  @import url('https://fonts.googleapis.com/css2?family=Space+Mono:wght@400;700&family=Syne:wght@400;600;800&display=swap');
  :root {
    --bg: #0a0f0a; --surface: #121a12; --surface2: #1a261a;
    --border: #2a3e2a; --accent: #6aff9b; --accent2: #ffb86a;
    --accent3: #6affda; --text: #e8f0e8; --text-muted: #6b8a6b;
  }
  * { margin:0; padding:0; box-sizing:border-box; }
  body { background:var(--bg); color:var(--text); font-family:'Syne',sans-serif; min-height:100vh; overflow-x:hidden; }
  body::before {
    content:''; position:fixed; inset:0;
    background-image: linear-gradient(rgba(106,255,155,0.03) 1px,transparent 1px), linear-gradient(90deg,rgba(106,255,155,0.03) 1px,transparent 1px);
    background-size:40px 40px; pointer-events:none; z-index:0;
  }
  .glow { position:fixed; width:600px; height:600px; border-radius:50%; filter:blur(120px); pointer-events:none; z-index:0; }
  .glow-1 { background:rgba(106,255,155,0.06); top:-200px; left:-200px; }
  .glow-2 { background:rgba(255,184,106,0.05); bottom:-200px; right:-200px; }
  .container { position:relative; z-index:1; max-width:1100px; margin:0 auto; padding:40px 24px; }
  header { display:flex; align-items:center; gap:16px; margin-bottom:60px; }
  .logo { width:44px; height:44px; background:linear-gradient(135deg,var(--accent),var(--accent2)); border-radius:12px; display:flex; align-items:center; justify-content:center; font-family:'Space Mono',monospace; font-size:18px; font-weight:700; flex-shrink:0; color:#0a0f0a; }
  .header-text h1 { font-size:22px; font-weight:800; letter-spacing:-0.5px; }
  .header-text p { font-size:13px; color:var(--text-muted); font-family:'Space Mono',monospace; margin-top:2px; }
  .status-badge { margin-left:auto; display:flex; align-items:center; gap:8px; background:var(--surface); border:1px solid var(--border); border-radius:100px; padding:8px 16px; font-size:13px; font-family:'Space Mono',monospace; }
  .dot { width:8px; height:8px; border-radius:50%; background:#3a4a3a; transition:background 0.3s; }
  .dot.online { background:var(--accent3); box-shadow:0 0 8px var(--accent3); animation:pulse 2s infinite; }
  .dot.offline { background:var(--accent2); }
  @keyframes pulse { 0%,100%{opacity:1} 50%{opacity:0.5} }
  .grid { display:grid; grid-template-columns:1fr 1fr; gap:24px; }
  .card { background:var(--surface); border:1px solid var(--border); border-radius:20px; padding:28px; position:relative; overflow:hidden; }
  .card::before { content:''; position:absolute; top:0; left:0; right:0; height:1px; background:linear-gradient(90deg,transparent,rgba(106,255,155,0.4),transparent); }
  .label { font-family:'Space Mono',monospace; font-size:11px; color:var(--accent); text-transform:uppercase; letter-spacing:2px; margin-bottom:16px; display:flex; align-items:center; gap:8px; }
  .label::before { content:'—'; opacity:0.5; }
  .upload-area { border:2px dashed var(--border); border-radius:14px; min-height:200px; display:flex; align-items:center; justify-content:center; cursor:pointer; transition:all 0.3s; flex-direction:column; gap:12px; color:var(--text-muted); font-size:14px; font-family:'Space Mono',monospace; }
  .upload-area:hover,.upload-area.drag { border-color:var(--accent); background:rgba(106,255,155,0.04); color:var(--text); }
  .upload-area img { max-width:100%; max-height:180px; border-radius:10px; object-fit:contain; }
  #fileInput { display:none; }
  .output-area { border:1px solid var(--border); border-radius:14px; min-height:200px; display:flex; align-items:center; justify-content:center; background:var(--surface2); position:relative; overflow:hidden; }
  .output-area img { max-width:100%; max-height:350px; border-radius:10px; object-fit:contain; }
  .overlay { position:absolute; inset:0; background:rgba(10,15,10,0.85); display:none; flex-direction:column; align-items:center; justify-content:center; gap:16px; border-radius:14px; }
  .overlay.active { display:flex; }
  .spinner { width:44px; height:44px; border:3px solid var(--border); border-top-color:var(--accent); border-radius:50%; animation:spin 0.8s linear infinite; }
  @keyframes spin { to{transform:rotate(360deg)} }
  .timer { font-family:'Space Mono',monospace; font-size:24px; color:var(--accent); }
  .loading-text { font-size:13px; color:var(--text-muted); font-family:'Space Mono',monospace; }
  .card.full { grid-column:1/-1; }
  textarea { width:100%; background:var(--surface2); border:1px solid var(--border); border-radius:12px; color:var(--text); padding:14px; font-family:'Space Mono',monospace; font-size:13px; resize:vertical; min-height:80px; outline:none; transition:border-color 0.3s; }
  textarea:focus { border-color:var(--accent); }
  .btn { width:100%; margin-top:16px; padding:16px; background:linear-gradient(135deg,var(--accent),var(--accent2)); border:none; border-radius:12px; color:#0a0f0a; font-family:'Syne',sans-serif; font-size:16px; font-weight:700; cursor:pointer; transition:all 0.3s; letter-spacing:0.5px; }
  .btn:hover:not(:disabled) { transform:translateY(-2px); box-shadow:0 8px 30px rgba(106,255,155,0.3); }
  .btn:disabled { opacity:0.5; cursor:not-allowed; }
  .btn-dl { display:none; width:100%; margin-top:12px; padding:14px; background:transparent; border:1px solid var(--accent); border-radius:12px; color:var(--accent); font-family:'Space Mono',monospace; font-size:13px; cursor:pointer; text-align:center; text-decoration:none; transition:all 0.3s; }
  .btn-dl:hover { background:rgba(106,255,155,0.1); }
  .log-box { background:var(--surface2); border:1px solid var(--border); border-radius:12px; padding:14px; font-family:'Space Mono',monospace; font-size:12px; max-height:150px; overflow-y:auto; }
  .log-line { padding:3px 0; border-bottom:1px solid rgba(255,255,255,0.04); }
  .log-line.ok { color:var(--accent); }
  .log-line.err { color:var(--accent2); }
  .log-line.info { color:var(--text-muted); }
  .model-badge { display:inline-block; background:rgba(106,255,155,0.1); border:1px solid var(--accent); border-radius:6px; padding:3px 10px; font-family:'Space Mono',monospace; font-size:11px; color:var(--accent); margin-left:8px; }
  .params-toggle { width:100%; padding:12px 16px; background:var(--surface2); border:1px solid var(--border); border-radius:12px; color:var(--accent); font-family:'Space Mono',monospace; font-size:13px; cursor:pointer; transition:all 0.3s; text-align:left; display:flex; align-items:center; gap:8px; }
  .params-toggle:hover { border-color:var(--accent); background:rgba(106,255,155,0.04); }
  .params-toggle .arrow { transition:transform 0.3s; display:inline-block; }
  .params-toggle.open .arrow { transform:rotate(90deg); }
  .params-panel { display:none; margin-top:12px; padding:20px; background:var(--surface2); border:1px solid var(--border); border-radius:12px; }
  .params-panel.open { display:block; }
  .param-row { display:flex; align-items:center; gap:16px; margin-bottom:16px; }
  .param-row:last-child { margin-bottom:0; }
  .param-label { font-family:'Space Mono',monospace; font-size:12px; color:var(--text-muted); min-width:110px; }
  .param-slider { flex:1; -webkit-appearance:none; appearance:none; height:6px; background:var(--border); border-radius:3px; outline:none; cursor:pointer; }
  .param-slider::-webkit-slider-thumb { -webkit-appearance:none; appearance:none; width:16px; height:16px; border-radius:50%; background:var(--accent); cursor:pointer; transition:box-shadow 0.2s; }
  .param-slider::-webkit-slider-thumb:hover { box-shadow:0 0 10px var(--accent); }
  .param-value { font-family:'Space Mono',monospace; font-size:13px; color:var(--accent); min-width:45px; text-align:right; }
  .toggle-container { display:flex; align-items:center; gap:10px; margin-top:12px; margin-bottom:12px; }
  .toggle-label { font-family:'Space Mono',monospace; font-size:13px; color:var(--text); }
  .switch { position:relative; display:inline-block; width:40px; height:22px; }
  .switch input { opacity:0; width:0; height:0; }
  .slider { position:absolute; cursor:pointer; top:0; left:0; right:0; bottom:0; background-color:var(--surface2); transition:.4s; border-radius:22px; border:1px solid var(--border); }
  .slider:before { position:absolute; content:""; height:14px; width:14px; left:3px; bottom:3px; background-color:var(--text-muted); transition:.4s; border-radius:50%; }
  input:checked + .slider { background-color:rgba(106,255,155,0.2); border-color:var(--accent); }
  input:checked + .slider:before { transform:translateX(18px); background-color:var(--accent); }
</style>
</head>
<body>
<div class="glow glow-1"></div>
<div class="glow glow-2"></div>
<div class="container">
  <header>
    <div class="logo">SD</div>
    <div class="header-text">
      <h1>Sketch → 3D Pipeline <span class="model-badge">SDXL</span></h1>
      <p>stable-diffusion-xl-base-1.0 · ControlNet Scribble</p>
    </div>
    <div class="status-badge">
      <div class="dot" id="dot"></div>
      <span id="statusText">Connessione...</span>
    </div>
  </header>

  <div class="grid">
    <div class="card">
      <div class="label">Input Sketch</div>
      <div class="upload-area" id="uploadArea">
        <span>🖊 Trascina lo sketch qui</span>
        <span style="font-size:12px">o clicca per selezionare</span>
      </div>
      <input type="file" id="fileInput" accept="image/*">
    </div>

    <div class="card">
      <div class="label">Immagine Generata</div>
      <div class="output-area" id="outputArea">
        <span style="color:var(--text-muted);font-size:13px;font-family:'Space Mono',monospace">In attesa di generazione...</span>
        <div class="overlay" id="overlay">
          <div class="spinner"></div>
          <div class="timer" id="timer">0s</div>
          <div class="loading-text">SDXL sta generando...</div>
        </div>
      </div>
      <a class="btn-dl" id="btnDl" download="generated_sdxl.png">⬇ Scarica immagine</a>
    </div>

    <div class="card full">
      <div class="label">Prompt</div>
      <textarea id="prompt" placeholder="Descrivi l'oggetto, es: a wooden chair, white background...">A banana</textarea>

      <div class="toggle-container">
        <label class="switch">
          <input type="checkbox" id="magicEnrich" checked>
          <span class="slider"></span>
        </label>
        <span class="toggle-label">Arricchimento Magico (Usa llama3.1 per espandere)</span>
      </div>

      <button class="params-toggle" id="paramsToggle" onclick="toggleParams()">
        <span class="arrow">▶</span> Parametri Avanzati
      </button>
      <div class="params-panel" id="paramsPanel">
        <div class="param-row">
          <span class="param-label">Strength</span>
          <input type="range" class="param-slider" id="strength" min="0" max="1" step="0.05" value="0.85" oninput="document.getElementById('strengthVal').textContent=this.value">
          <span class="param-value" id="strengthVal">0.85</span>
        </div>
        <div class="param-row">
          <span class="param-label">End Percent</span>
          <input type="range" class="param-slider" id="endPercent" min="0" max="1" step="0.05" value="0.9" oninput="document.getElementById('endPercentVal').textContent=this.value">
          <span class="param-value" id="endPercentVal">0.9</span>
        </div>
        <div class="param-row">
          <span class="param-label">CFG</span>
          <input type="range" class="param-slider" id="cfg" min="1" max="15" step="0.5" value="7" oninput="document.getElementById('cfgVal').textContent=this.value">
          <span class="param-value" id="cfgVal">7</span>
        </div>
        <div class="param-row">
          <span class="param-label">Steps</span>
          <input type="range" class="param-slider" id="steps" min="10" max="50" step="1" value="20" oninput="document.getElementById('stepsVal').textContent=this.value">
          <span class="param-value" id="stepsVal">20</span>
        </div>
      </div>

      <button class="btn" id="btnGen" onclick="generate()">✦ Genera con SDXL</button>
    </div>

    <div class="card full">
      <div class="label">Log</div>
      <div class="log-box" id="log">
        <div class="log-line info">→ SDXL pronto. Carica uno sketch e premi Genera.</div>
      </div>
    </div>
  </div>
</div>

<script>
  let imgB64 = null, timerInt = null, secs = 0;

  async function checkStatus() {
    try {
      const r = await fetch('/health');
      const d = await r.json();
      document.getElementById('dot').className = 'dot ' + (d.status==='ok' ? 'online' : 'offline');
      document.getElementById('statusText').textContent = d.status==='ok' ? 'Server attivo' : 'Errore';
    } catch { document.getElementById('dot').className='dot offline'; document.getElementById('statusText').textContent='Offline'; }
  }
  checkStatus(); setInterval(checkStatus, 10000);

  const area = document.getElementById('uploadArea');
  area.addEventListener('click', () => document.getElementById('fileInput').click());
  area.addEventListener('dragover', e => { e.preventDefault(); area.classList.add('drag'); });
  area.addEventListener('dragleave', () => area.classList.remove('drag'));
  area.addEventListener('drop', e => { e.preventDefault(); area.classList.remove('drag'); if(e.dataTransfer.files[0]) loadFile(e.dataTransfer.files[0]); });
  document.getElementById('fileInput').addEventListener('change', e => { if(e.target.files[0]) loadFile(e.target.files[0]); });

  function loadFile(file) {
    const r = new FileReader();
    r.onload = e => {
      imgB64 = e.target.result.split(',')[1];
      area.innerHTML = `<img src="${e.target.result}" alt="sketch">`;
      log('Sketch caricato: ' + file.name, 'ok');
    };
    r.readAsDataURL(file);
  }

  function log(msg, type='') {
    const box = document.getElementById('log');
    const div = document.createElement('div');
    div.className = 'log-line ' + type;
    div.textContent = `[${new Date().toLocaleTimeString('it-IT')}] ${msg}`;
    box.appendChild(div); box.scrollTop = box.scrollHeight;
  }

  async function generate() {
    if (!imgB64) { log('⚠ Carica prima uno sketch!', 'err'); return; }
    const btn = document.getElementById('btnGen');
    const overlay = document.getElementById('overlay');
    const btnDl = document.getElementById('btnDl');
    btn.disabled = true; overlay.classList.add('active'); btnDl.style.display='none';
    secs=0; document.getElementById('timer').textContent='0s';
    timerInt = setInterval(() => { secs++; document.getElementById('timer').textContent=secs+'s'; }, 1000);
    log('→ Invio sketch a ComfyUI (SDXL)...', 'info');
    try {
      const params = {
        image: imgB64,
        prompt: document.getElementById('prompt').value,
        magic_enrich: document.getElementById('magicEnrich').checked,
        strength: parseFloat(document.getElementById('strength').value),
        end_percent: parseFloat(document.getElementById('endPercent').value),
        cfg: parseFloat(document.getElementById('cfg').value),
        steps: parseInt(document.getElementById('steps').value)
      };
      log('Parametri: strength=' + params.strength + ' end%=' + params.end_percent + ' cfg=' + params.cfg + ' steps=' + params.steps, 'info');
      const res = await fetch('/generate', {
        method:'POST', headers:{'Content-Type':'application/json'},
        body: JSON.stringify(params)
      });
      const data = await res.json();
      clearInterval(timerInt); overlay.classList.remove('active');
      if (data.status === 'success') {
        const src = 'data:image/png;base64,' + data.image;
        document.getElementById('outputArea').innerHTML = `<img src="${src}" alt="risultato"><div class="overlay" id="overlay"><div class="spinner"></div><div class="timer" id="timer">0s</div><div class="loading-text">SDXL sta generando...</div></div>`;
        btnDl.style.display='block'; btnDl.href=src;
        log('✓ Immagine SDXL generata in ' + secs + 's', 'ok');
        if (data.prompt_usato) log('Prompt espanso: ' + data.prompt_usato.substring(0,80) + '...', 'info');
      } else { log('✗ Errore: ' + data.message, 'err'); }
    } catch(e) { clearInterval(timerInt); overlay.classList.remove('active'); log('✗ Connessione fallita: '+e.message,'err'); }
    btn.disabled = false;
  }

  function toggleParams() {
    const btn = document.getElementById('paramsToggle');
    const panel = document.getElementById('paramsPanel');
    btn.classList.toggle('open');
    panel.classList.toggle('open');
  }
</script>
</body>
</html>
"""

# ==========================================
# WORKFLOW SDXL CON CONTROLNET SCRIBBLE
# Differenze rispetto a FLUX:
# - CheckpointLoaderSimple (carica tutto in un nodo solo)
# - Niente FluxGuidance (SDXL usa il CFG standard)
# - CFG = 7.0 (parametro classico di Stable Diffusion)
# - Sampler: dpmpp_2m con scheduler karras (ottimale per SDXL)
# - Steps: 20 (SDXL non ha bisogno di più)
# ==========================================
def build_workflow(image_base64, prompt_text, strength=0.85, start_percent=0.0, end_percent=0.9, steps=20, cfg=7.0):
    return {
        # Carica modello SDXL, CLIP e VAE in un unico nodo
        "1": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": "sdxl-base-1.0.safetensors"}},

        # Prompt positivo e negativo usando il CLIP di SDXL
        "4": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["1", 1], "text": prompt_text}},
        "5": {"class_type": "CLIPTextEncode", "inputs": {"clip": ["1", 1], "text": "background, colored background, gradient background, patterned background, textured background, geometric background, wallpaper, environment, scenery, landscape, dark background, busy background, colorful background, abstract background, bokeh, blurry, low quality, distorted, deformed, text, watermark, multiple objects, extra limbs, ugly, disfigured, shadow, reflection, floor, surface, table"}},

        # Carica immagine e applicale il preprocessore Scribble
        "6": {"class_type": "ETN_LoadImageBase64", "inputs": {"image": image_base64}},
        "7": {"class_type": "AIO_Preprocessor", "inputs": {"image": ["6", 0], "preprocessor": "ScribblePreprocessor", "resolution": 1024}},

        # Carica il ControlNet specifico per SDXL
        "8": {"class_type": "ControlNetLoader", "inputs": {"control_net_name": "sdxl_scribble_controlnet.safetensors"}},

        # Applica il ControlNet al prompt positivo e negativo
        "9": {"class_type": "ControlNetApplyAdvanced", "inputs": {
            "positive": ["4", 0], "negative": ["5", 0], "control_net": ["8", 0],
            "image": ["7", 0],
            "strength": strength, "start_percent": start_percent, "end_percent": end_percent
        }},

        # Immagine vuota 1024x1024 (risoluzione nativa SDXL)
        "10": {"class_type": "EmptyLatentImage", "inputs": {"width": 1024, "height": 1024, "batch_size": 1}},

        # KSampler con parametri ottimali per SDXL
        "11": {
            "class_type": "KSampler",
            "inputs": {
                "model": ["1", 0],
                "positive": ["9", 0],
                "negative": ["9", 1],
                "latent_image": ["10", 0],
                "seed": random.randint(0, 2**32 - 1),
                "steps": steps,
                "cfg": cfg,              # CFG = 7.0 per SDXL (NON 1.0 come in FLUX)
                "sampler_name": "dpmpp_2m",   # Sampler ottimale per SDXL
                "scheduler": "karras",         # Scheduler ottimale per SDXL
                "denoise": 1.0
            }
        },

        # Decodifica il latente in immagine usando il VAE di SDXL
        "12": {"class_type": "VAEDecode", "inputs": {"samples": ["11", 0], "vae": ["1", 2]}},
        "13": {"class_type": "SaveImage", "inputs": {"images": ["12", 0], "filename_prefix": "flask_sdxl_output"}}
    }


import ollama

def espandi_prompt(prompt_semplice, magic_enrich=True):
    # Template fisso testato per la ricostruzione 3D
    template_fisso = f'A high-quality image of a {prompt_semplice.strip()}, physically plausible materials, suitable for 3D reconstruction, centered, isolated on a pure white background, clean studio lighting, sharp focus, no shadows, single object, product photography style'

    if not magic_enrich:
        print(f"\n[PROMPT OFF] Originale: {prompt_semplice}")
        print(f"[PROMPT OFF] Espanso: {template_fisso}\n")
        return template_fisso

    # Se l'arricchimento è ON, usiamo llama3.1
    # IMPORTANTE: chiediamo ad Ollama di descrivere SOLO l'oggetto,
    # poi noi lo "incapsuliamo" nel template fisso con sfondo bianco.
    # Questo impedisce che i colori vivaci descritti da Ollama
    # vengano proiettati sullo sfondo da SDXL.
    system_instruction = (
        "You are a product detail writer. The user gives you an object name. "
        "Your job is to write a short, vivid description of that object's visual characteristics ONLY: "
        "its texture, material, surface details, and distinctive features. "
        "STRICT RULES: "
        "1. Describe ONLY the object itself. No background, no setting, no environment, no floor, no table, no shadow, no lighting. "
        "2. DO NOT change the core meaning or color of the object. "
        "3. Keep it under 40 words. "
        "4. Output ONLY the raw description. NO intro text like 'Here is' or 'Output:'."
    )
    
    try:
        response = ollama.generate(
            model='llama3.1',
            prompt=f"{system_instruction}\nObject: {prompt_semplice}"
        )
        # Ollama descrive solo l'oggetto, noi aggiungiamo il template con sfondo bianco
        object_description = response['response'].strip().replace('\n', ' ').rstrip(",. ")
        
        # Il template fisso garantisce sempre sfondo bianco, indipendentemente da Ollama
        expanded = (
            f"A high-quality product photograph of {object_description}, "
            f"isolated on a pure solid white background, clean studio lighting, "
            f"sharp focus, no shadows, no reflections, single object, product photography style"
        )
        
        print(f"\n[OLLAMA 8B] Originale: {prompt_semplice}")
        print(f"[OLLAMA 8B] Descrizione oggetto: {object_description}")
        print(f"[OLLAMA 8B] Prompt finale: {expanded}\n")
        return expanded
        
    except Exception as e:
        print(f"Errore Ollama llama3.1: {e}. Fallback al template fisso.")
        return template_fisso


def center_and_crop_image(base64_str, canvas_size=1024, padding=60):
    """
    Riceve un'immagine in base64 (lo sketch), trova i bordi effettivi del disegno, 
    la ritaglia per eliminare lo spazio vuoto inutile, e la riposiziona 
    esattamente al centro di un quadrato bianco 1024x1024.
    """
    try:
        # Decodifica base64 a immagine OpenCV
        img_data = base64.b64decode(base64_str)
        np_arr = np.frombuffer(img_data, np.uint8)
        img = cv2.imdecode(np_arr, cv2.IMREAD_UNCHANGED)
        
        if img is None:
            return base64_str
            
        # 1. Crea la maschera per trovare il disegno
        if len(img.shape) == 3 and img.shape[2] == 4:
            # Se ha un canale alpha (trasparenza), usiamo quello come maschera
            alpha = img[:, :, 3]
            _, mask = cv2.threshold(alpha, 10, 255, cv2.THRESH_BINARY)
            # Appiattiamo su sfondo bianco
            bgr = img[:, :, :3]
            bg = np.ones_like(bgr, dtype=np.uint8) * 255
            img = np.where(mask[:,:,None] == 255, bgr, bg)
        else:
            # Se è RGB, convertiamo in scala di grigi
            gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
            # Immaginiamo linee nere su sfondo bianco -> invertiamo per avere linee bianche
            inv = cv2.bitwise_not(gray)
            _, mask = cv2.threshold(inv, 10, 255, cv2.THRESH_BINARY)
            
        # 2. Trova il bounding box (scatola di delimitazione)
        coords = cv2.findNonZero(mask)
        if coords is None:
            return base64_str # Immagine vuota
            
        x, y, w, h = cv2.boundingRect(coords)
        
        # 3. Ritaglia esattamente il disegno
        cropped = img[y:y+h, x:x+w]
        
        # 4. Calcola il ridimensionamento mantenendo le proporzioni
        target_inner_size = canvas_size - (padding * 2)
        scale = min(target_inner_size / w, target_inner_size / h)
        new_w = int(w * scale)
        new_h = int(h * scale)
        
        resized = cv2.resize(cropped, (new_w, new_h), interpolation=cv2.INTER_AREA)
        
        # 5. Crea la tela bianca finale (1024x1024)
        canvas = np.ones((canvas_size, canvas_size, 3), dtype=np.uint8) * 255
        
        # 6. Calcola le coordinate per posizionarlo esattamente al centro
        start_y = (canvas_size - new_h) // 2
        start_x = (canvas_size - new_w) // 2
        
        # Incolla il disegno ridimensionato sulla tela
        canvas[start_y:start_y+new_h, start_x:start_x+new_w] = resized
        
        # Codifica di nuovo in base64
        _, buffer = cv2.imencode('.png', canvas)
        return base64.b64encode(buffer).decode('utf-8')
    except Exception as e:
        print(f"Errore durante il crop OpenCV: {e}")
        return base64_str # In caso di errore, torna l'immagine originale

def queue_prompt(workflow):
    client_id = str(uuid.uuid4())
    r = requests.post(f"{COMFYUI_URL}/prompt", json={"prompt": workflow, "client_id": client_id})
    r.raise_for_status()
    return r.json()["prompt_id"]

def wait_for_result(prompt_id, timeout=1000):
    start = time.time()
    while time.time() - start < timeout:
        r = requests.get(f"{COMFYUI_URL}/history/{prompt_id}")
        h = r.json()
        if prompt_id in h:
            return h[prompt_id]
        time.sleep(2)
    return None

def get_image_base64(filename, subfolder="", folder_type="output"):
    r = requests.get(f"{COMFYUI_URL}/view", params={"filename": filename, "subfolder": subfolder, "type": folder_type})
    r.raise_for_status()
    return base64.b64encode(r.content).decode("utf-8")

@app.route("/")
def index():
    return render_template_string(HTML_PAGE)

@app.route("/health", methods=["GET"])
def health():
    return jsonify({"status": "ok", "message": "Flask SDXL API attiva"})

@app.route("/generate", methods=["POST"])
def generate():
    try:
        data = request.get_json()
        if not data or "image" not in data:
            return jsonify({"status": "error", "message": "Campo 'image' mancante"}), 400

        print(f"\n[Flask SDXL] Generazione avviata...")

        prompt_semplice = data.get("prompt", "").strip()
        if not prompt_semplice:
            prompt_semplice = "an object"

        # Parametri SDXL
        strength     = float(data.get("strength", 0.85))
        start_percent= float(data.get("start_percent", 0.0))
        end_percent  = float(data.get("end_percent", 0.9))
        steps        = int(data.get("steps", 20))
        cfg          = float(data.get("cfg", 7.0))

        magic_enrich = data.get("magic_enrich", True)

        prompt_ottimizzato = espandi_prompt(prompt_semplice, magic_enrich)
        
        # 1. Ritaglia e centra l'immagine con OpenCV
        print("[Flask SDXL] Ritaglio e centratura dello sketch con OpenCV...")
        image_centered_base64 = center_and_crop_image(data["image"])
        
        # 2. Costruisci il workflow con l'immagine centrata
        workflow = build_workflow(image_centered_base64, prompt_ottimizzato, strength, start_percent, end_percent, steps, cfg)

        prompt_id = queue_prompt(workflow)
        print(f"[Flask SDXL] ID Task ComfyUI: {prompt_id}")

        result = wait_for_result(prompt_id)
        if not result:
            return jsonify({"status": "error", "message": "Timeout"}), 504

        for _, node_output in result.get("outputs", {}).items():
            if "images" in node_output:
                img = node_output["images"][0]
                img_data = get_image_base64(img["filename"], img.get("subfolder",""), img.get("type","output"))
                return jsonify({
                    "status": "success",
                    "image": img_data,
                    "prompt_usato": prompt_ottimizzato
                })

        return jsonify({"status": "error", "message": "Nessuna immagine generata"}), 500

    except requests.exceptions.ConnectionError:
        return jsonify({"status": "error", "message": "ComfyUI non raggiungibile"}), 503
    except Exception as e:
        return jsonify({"status": "error", "message": str(e)}), 500

if __name__ == "__main__":
    print("=" * 50)
    print("  Flask API · SDXL Sketch → 3D Pipeline")
    print("=" * 50)
    print("  Apri nel browser: http://127.0.0.1:5001")
    print("=" * 50)
    app.run(host="0.0.0.0", port=5001, debug=False)
