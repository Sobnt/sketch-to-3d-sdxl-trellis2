import argparse
import base64
import copy
import json
import os
import threading
import time
import uuid
from pathlib import Path

import requests
from flask import Flask, jsonify, request, send_file


app = Flask(__name__)
args = None
jobs = {}
jobs_lock = threading.Lock()


def now_message(message):
    return time.strftime("[%H:%M:%S] ") + message


def set_job(job_id, **updates):
    with jobs_lock:
        job = jobs.setdefault(job_id, {})
        job.update(updates)
        job["updated_at"] = time.time()
        return dict(job)


def get_job(job_id):
    with jobs_lock:
        job = jobs.get(job_id)
        return dict(job) if job else None


@app.get("/health")
def health():
    comfy_ok = False
    try:
        response = requests.get(args.comfy_url.rstrip("/") + "/system_stats", timeout=2)
        comfy_ok = response.ok
    except requests.RequestException:
        comfy_ok = False

    return jsonify({
        "status": "ok",
        "service": "trellis-bridge",
        "comfyui_reachable": comfy_ok,
    })


@app.post("/generate-3d")
def generate_3d():
    body = request.get_json(silent=True) or {}
    image_base64 = body.get("image", "")
    file_name = body.get("file_name") or "unity_trellis_input.png"

    if not image_base64:
        return jsonify({
            "status": "error",
            "message": "Campo image mancante.",
        }), 400

    try:
        image_bytes = base64.b64decode(remove_data_url_prefix(image_base64))
    except Exception as exc:
        return jsonify({
            "status": "error",
            "message": f"Base64 immagine non valido: {exc}",
        }), 400

    job_id = uuid.uuid4().hex
    set_job(
        job_id,
        status="queued",
        message="Job Trellis creato.",
        file_name=file_name,
        created_at=time.time(),
    )

    worker = threading.Thread(target=run_job, args=(job_id, image_bytes, file_name), daemon=True)
    worker.start()

    return jsonify({
        "status": "queued",
        "job_id": job_id,
        "message": "Generazione 3D avviata.",
    }), 202


@app.get("/jobs/<job_id>")
def job_status(job_id):
    job = get_job(job_id)
    if not job:
        return jsonify({
            "status": "error",
            "message": "Job non trovato.",
        }), 404

    response = public_job_response(job_id, job)
    return jsonify(response)


@app.get("/download/<job_id>")
def download(job_id):
    job = get_job(job_id)
    if not job or job.get("status") != "success":
        return jsonify({
            "status": "error",
            "message": "Modello non disponibile.",
        }), 404

    model_path = job.get("model_path", "")
    if not model_path or not os.path.exists(model_path):
        return jsonify({
            "status": "error",
            "message": "File GLB non trovato sul bridge.",
        }), 404

    return send_file(model_path, as_attachment=True, download_name=os.path.basename(model_path))


def run_job(job_id, image_bytes, file_name):
    try:
        job_started_at = time.time()
        set_job(job_id, status="processing", message=now_message("Upload immagine a ComfyUI..."))
        uploaded_name = upload_image_to_comfy(image_bytes, file_name)

        set_job(job_id, message=now_message("Preparazione workflow Trellis..."))
        workflow = load_workflow()
        patch_workflow_image(workflow, uploaded_name)

        set_job(job_id, message=now_message("Invio workflow a ComfyUI..."))
        prompt_id = queue_prompt(workflow)

        set_job(job_id, message=now_message("ComfyUI sta generando il modello 3D..."), prompt_id=prompt_id)
        history = wait_for_history(prompt_id)
        save_history_debug(job_id, history)

        model_info = find_model_file(history)
        if not model_info:
            model_info = find_recent_output_model(job_started_at)

        if not model_info:
            raise RuntimeError(
                "Nessun file 3D trovato nella history o nella cartella output di ComfyUI. "
                "Controlla che il workflow salvi davvero un GLB con un nodo di export/save."
            )

        set_job(job_id, message=now_message("Download GLB da ComfyUI..."))
        model_bytes = download_comfy_file(model_info)

        output_dir = Path(args.output_dir)
        output_dir.mkdir(parents=True, exist_ok=True)
        output_name = safe_model_name(model_info.get("filename") or f"trellis_{job_id}.glb")
        output_path = output_dir / output_name
        output_path.write_bytes(model_bytes)

        set_job(
            job_id,
            status="success",
            message="Modello 3D pronto.",
            model_path=str(output_path),
            file_name=output_path.name,
            download_url=f"/download/{job_id}",
        )
    except Exception as exc:
        set_job(job_id, status="error", message=str(exc))


def upload_image_to_comfy(image_bytes, file_name):
    url = args.comfy_url.rstrip("/") + "/upload/image"
    clean_name = safe_image_name(file_name)
    files = {
        "image": (clean_name, image_bytes, "image/png"),
    }
    data = {
        "type": "input",
        "overwrite": "true",
    }

    response = requests.post(url, files=files, data=data, timeout=60)
    response.raise_for_status()
    payload = response.json()
    return payload.get("name") or clean_name


def load_workflow():
    workflow_path = Path(args.workflow)
    if not workflow_path.exists():
        raise FileNotFoundError(f"Workflow API non trovato: {workflow_path}")

    with workflow_path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def patch_workflow_image(workflow, uploaded_name):
    workflow_copy = copy.deepcopy(workflow)
    patched = False

    if args.image_node_id:
        node = workflow_copy.get(args.image_node_id)
        if not node:
            raise KeyError(f"Nodo immagine {args.image_node_id} non trovato nel workflow.")
        inputs = node.setdefault("inputs", {})
        inputs["image"] = uploaded_name
        patched = True
    else:
        for node in workflow_copy.values():
            if not isinstance(node, dict):
                continue
            inputs = node.get("inputs", {})
            class_type = node.get("class_type", "")
            if "image" in inputs and ("LoadImage" in class_type or "Load Image" in class_type or class_type == "LoadImage"):
                inputs["image"] = uploaded_name
                patched = True

    if not patched:
        raise RuntimeError(
            "Non ho trovato un nodo LoadImage da aggiornare. Avvia il bridge con --image-node-id ID_DEL_NODO."
        )

    workflow.clear()
    workflow.update(workflow_copy)


def queue_prompt(workflow):
    url = args.comfy_url.rstrip("/") + "/prompt"
    payload = {
        "prompt": workflow,
        "client_id": uuid.uuid4().hex,
    }
    response = requests.post(url, json=payload, timeout=60)
    response.raise_for_status()
    data = response.json()
    prompt_id = data.get("prompt_id")
    if not prompt_id:
        raise RuntimeError(f"ComfyUI non ha restituito prompt_id: {data}")
    return prompt_id


def wait_for_history(prompt_id):
    url = args.comfy_url.rstrip("/") + f"/history/{prompt_id}"
    deadline = time.time() + args.timeout

    while time.time() < deadline:
        response = requests.get(url, timeout=30)
        if response.ok:
            payload = response.json()
            if prompt_id in payload:
                return payload[prompt_id]
            if payload:
                return payload
        time.sleep(args.poll_interval)

    raise TimeoutError("Timeout in attesa della history ComfyUI.")


def find_model_file(history):
    candidates = []

    def visit(value):
        if isinstance(value, dict):
            filename = value.get("filename")
            if isinstance(filename, str) and filename.lower().endswith((".glb", ".obj", ".fbx", ".ply")):
                candidates.append(value)
            for key, child in value.items():
                if isinstance(child, str) and child.lower().endswith((".glb", ".obj", ".fbx", ".ply")):
                    candidates.append({
                        "filename": os.path.basename(child),
                        "local_path": child if os.path.isabs(child) else "",
                        "subfolder": "",
                        "type": "output",
                        "source_key": key,
                    })
            for child in value.values():
                visit(child)
        elif isinstance(value, list):
            for child in value:
                visit(child)

    visit(history)

    for candidate in candidates:
        if candidate.get("filename", "").lower().endswith(".glb"):
            return candidate

    return candidates[0] if candidates else None


def find_recent_output_model(job_started_at):
    output_dir = get_comfy_output_dir()
    if not output_dir.exists():
        return None

    candidates = []
    for extension in ("*.glb", "*.obj", "*.fbx", "*.ply"):
        candidates.extend(output_dir.rglob(extension))

    recent_candidates = [
        path for path in candidates
        if path.is_file() and path.stat().st_mtime >= job_started_at - 5
    ]

    if not recent_candidates:
        return None

    newest = max(recent_candidates, key=lambda path: path.stat().st_mtime)
    try:
        subfolder = str(newest.parent.relative_to(output_dir))
        if subfolder == ".":
            subfolder = ""
    except ValueError:
        subfolder = ""

    return {
        "filename": newest.name,
        "local_path": str(newest),
        "subfolder": subfolder,
        "type": "output",
    }


def download_comfy_file(model_info):
    local_path = model_info.get("local_path", "")
    if local_path and os.path.exists(local_path):
        return Path(local_path).read_bytes()

    params = {
        "filename": model_info.get("filename", ""),
        "subfolder": model_info.get("subfolder", ""),
        "type": model_info.get("type", "output"),
    }
    response = requests.get(args.comfy_url.rstrip("/") + "/view", params=params, timeout=120)
    response.raise_for_status()
    return response.content


def save_history_debug(job_id, history):
    output_dir = Path(args.output_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    debug_path = output_dir / f"history_{job_id}.json"
    with debug_path.open("w", encoding="utf-8") as handle:
        json.dump(history, handle, indent=2, ensure_ascii=False)


def get_comfy_output_dir():
    if args.comfy_output_dir:
        return Path(args.comfy_output_dir)

    workflow_dir = Path(args.workflow).resolve().parent
    guessed = workflow_dir / "ComfyUI" / "output"
    if guessed.exists():
        return guessed

    return Path.cwd().parent / "ComfyUI" / "output"


def public_job_response(job_id, job):
    response = {
        "status": job.get("status", "unknown"),
        "job_id": job_id,
        "message": job.get("message", ""),
        "file_name": job.get("file_name", ""),
    }

    if job.get("status") == "success":
        response["download_url"] = job.get("download_url", f"/download/{job_id}")
        response["model_path"] = job.get("model_path", "")

    return response


def remove_data_url_prefix(value):
    comma_index = value.find(",")
    return value[comma_index + 1:] if comma_index >= 0 else value


def safe_image_name(file_name):
    name = os.path.basename(file_name or "unity_trellis_input.png")
    if not name.lower().endswith(".png"):
        name = os.path.splitext(name)[0] + ".png"
    return name


def safe_model_name(file_name):
    name = os.path.basename(file_name or "trellis_model.glb")
    if not name.lower().endswith(".glb"):
        name = os.path.splitext(name)[0] + ".glb"
    return name


def parse_args():
    parser = argparse.ArgumentParser(description="Flask bridge between Unity and ComfyUI Trellis.")
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=5055)
    parser.add_argument("--comfy-url", default="http://127.0.0.1:8188")
    parser.add_argument("--workflow", default="trellis_workflow_api.json")
    parser.add_argument("--image-node-id", default="")
    parser.add_argument("--output-dir", default="trellis_bridge_outputs")
    parser.add_argument("--comfy-output-dir", default="")
    parser.add_argument("--timeout", type=int, default=900)
    parser.add_argument("--poll-interval", type=float, default=2.0)
    return parser.parse_args()


if __name__ == "__main__":
    args = parse_args()
    Path(args.output_dir).mkdir(parents=True, exist_ok=True)
    app.run(host=args.host, port=args.port, threaded=True)
