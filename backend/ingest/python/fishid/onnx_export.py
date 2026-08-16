"""Экспорт обученной модели в ONNX с препроцессингом внутри графа.

Вход ONNX-графа: float [1, 224, 224, 3], пиксели 0..255, RGB.
Выход: logits [1, num_classes] (без softmax — его делает C#).

Использование:
    python onnx_export.py --weights data/fishid/runs/run1/best.pt \
        --labels data/fishid/runs/run1/classes.json \
        --out backend/src/Klevo.Api/wwwroot/models/fishid/model.onnx
"""

import argparse
import json
import os

import torch
import torch.nn as nn
from torchvision import models

MEAN = (0.485, 0.456, 0.406)
STD = (0.229, 0.224, 0.225)


class PreprocBackbone(nn.Module):
    """Принимает float NHWC [N,224,224,3] (0..255), нормализует и прогоняет backbone."""

    def __init__(self, backbone):
        super().__init__()
        self.backbone = backbone
        self.register_buffer("mean", torch.tensor(MEAN).view(1, 3, 1, 1))
        self.register_buffer("std", torch.tensor(STD).view(1, 3, 1, 1))

    def forward(self, x):
        x = x.permute(0, 3, 1, 2).contiguous()
        x = x / 255.0
        x = (x - self.mean) / self.std
        return self.backbone(x)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--weights", required=True)
    parser.add_argument("--labels", required=True, help="JSON: {class_name: {name_ru, name_latin}}")
    parser.add_argument("--order", default=None,
                        help="Файл с порядком классов, в котором обучена модель (по умолчанию — сортировка, как у ImageFolder)")
    parser.add_argument("--out", default="backend/src/Klevo.Api/wwwroot/models/fishid/model.onnx")
    parser.add_argument("--img-size", type=int, default=224)
    parser.add_argument("--opset", type=int, default=17)
    parser.add_argument("--arch", default="mobilenet_v3_small",
                        choices=["mobilenet_v3_small", "mobilenet_v3_large"])
    args = parser.parse_args()

    with open(args.labels, encoding="utf-8") as f:
        labels = json.load(f)
    if args.order:
        with open(args.order, encoding="utf-8") as f:
            classes = [line.strip() for line in f if line.strip()]
    else:
        classes = sorted(labels.keys())

    backbone = models.__dict__[args.arch](weights=None)
    backbone.classifier[3] = nn.Linear(backbone.classifier[3].in_features, len(classes))
    backbone.load_state_dict(torch.load(args.weights, weights_only=True, map_location="cpu"))
    backbone.eval()

    wrapped = PreprocBackbone(backbone).eval()
    dummy = torch.rand(1, args.img_size, args.img_size, 3)
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    torch.onnx.export(
        wrapped, dummy, args.out,
        input_names=["image"],
        output_names=["logits"],
        opset_version=args.opset,
        dynamic_axes=None,
    )

    classes_txt = os.path.join(os.path.dirname(args.out), "classes.txt")
    with open(classes_txt, "w", encoding="utf-8") as f:
        for i, c in enumerate(classes):
            m = labels[c]
            f.write(f"{i}|{m['name_latin']}|{m['name_ru']}\n")
    print("exported:", args.out)
    print("classes :", classes_txt, f"({len(classes)} classes)")


if __name__ == "__main__":
    main()
