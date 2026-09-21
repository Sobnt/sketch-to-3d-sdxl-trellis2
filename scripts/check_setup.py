"""Check local workflow files and a running ComfyUI before launching the servers."""

import argparse
import json
import sys
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.request import urlopen


ROOT = Path(__file__).resolve().parents[1]
WORKFLOWS = {
    "2d": ROOT / "ComfyUI_Workflows/Workflow_SDXL/sdxl_scribble_workflow.json",
    "3d": ROOT / "ComfyUI_Workflows/Workflow_Trellis2/trellis_workflow_api.json",
}
EXTRA_2D_NODES = {"ETN_LoadImageBase64"}


def load_json(path):
    with path.open(encoding="utf-8") as handle:
        return json.load(handle)


def fetch_json(url):
    with urlopen(url, timeout=10) as response:
        return json.load(response)


def model_options(node_info, input_name):
    required = node_info.get("input", {}).get("required", {})
    definition = required.get(input_name, [])
    if definition and isinstance(definition[0], list):
        return definition[0]
    return []


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("phase", choices=("2d", "3d"))
    parser.add_argument("--comfy-url", default="http://127.0.0.1:8188")
    args = parser.parse_args()

    workflow_path = WORKFLOWS[args.phase]
    try:
        workflow = load_json(workflow_path)
    except (OSError, ValueError) as exc:
        print(f"ERRORE: workflow non leggibile: {workflow_path}: {exc}")
        return 1

    required_nodes = {node["class_type"] for node in workflow.values()}
    if args.phase == "2d":
        required_nodes |= EXTRA_2D_NODES

    try:
        object_info = fetch_json(args.comfy_url.rstrip("/") + "/object_info")
    except (HTTPError, URLError, TimeoutError, ValueError) as exc:
        print(f"ERRORE: ComfyUI non raggiungibile su {args.comfy_url}: {exc}")
        return 1

    missing = sorted(required_nodes - object_info.keys())
    print(f"Workflow {args.phase}: {workflow_path}")
    print(f"Nodi richiesti: {len(required_nodes)}; mancanti: {len(missing)}")
    for name in missing:
        print(f"  MANCA nodo: {name}")

    if args.phase == "2d":
        model_checks = (
            ("CheckpointLoaderSimple", "ckpt_name", "sdxl-base-1.0.safetensors"),
            ("ControlNetLoader", "control_net_name", "sdxl_scribble_controlnet.safetensors"),
        )
        for node_name, input_name, filename in model_checks:
            if node_name in object_info:
                choices = model_options(object_info[node_name], input_name)
                if filename in choices:
                    print(f"OK modello: {filename}")
                else:
                    print(f"MANCA modello selezionabile: {filename}")
                    missing.append(filename)

        preprocessors = model_options(object_info.get("AIO_Preprocessor", {}), "preprocessor")
        if "AIO_Preprocessor" in object_info and "ScribblePreprocessor" not in preprocessors:
            print("MANCA preprocessore selezionabile: ScribblePreprocessor")
            missing.append("ScribblePreprocessor")

    if args.phase == "3d":
        print("NOTA: i pesi TRELLIS.2 e la compatibilita GPU non sono verificabili da questo controllo.")

    if missing:
        print("Preflight non superato.")
        return 1
    print("Preflight superato; eseguire comunque un test di generazione completo.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
