"""Fine-tune MobileNetV3-Large на датасете рыб (ImageFolder из download_wikimedia.py).

Использование:
    python train.py --data data/fishid/raw --epochs 14 --out data/fishid/runs/run1
"""

import argparse
import json
import os
import random
import sys
import time

import numpy as np
import torch
import torch.nn as nn
from torch.utils.data import DataLoader, Subset, random_split
from torchvision import datasets, models, transforms

IMAGENET_MEAN = (0.485, 0.456, 0.406)
IMAGENET_STD = (0.229, 0.224, 0.225)

train_tf = transforms.Compose([
    transforms.RandomResizedCrop(224, scale=(0.6, 1.0)),
    transforms.RandomHorizontalFlip(),
    transforms.ColorJitter(brightness=0.25, contrast=0.25, saturation=0.2),
    transforms.ToTensor(),
    transforms.Normalize(IMAGENET_MEAN, IMAGENET_STD),
])

eval_tf = transforms.Compose([
    transforms.Resize(256),
    transforms.CenterCrop(224),
    transforms.ToTensor(),
    transforms.Normalize(IMAGENET_MEAN, IMAGENET_STD),
])


def split_indices(ds, val_frac=0.1, test_frac=0.1, seed=7):
    rng = random.Random(seed)
    idx_by_class = {}
    for i, (_, y) in enumerate(ds.samples):
        idx_by_class.setdefault(y, []).append(i)
    tr, va, te = [], [], []
    for cls, idxs in idx_by_class.items():
        rng.shuffle(idxs)
        n = len(idxs)
        n_te = max(1, int(round(n * test_frac)))
        n_va = max(1, int(round(n * val_frac)))
        te += idxs[:n_te]
        va += idxs[n_te:n_te + n_va]
        tr += idxs[n_te + n_va:]
    return sorted(tr), sorted(va), sorted(te)


def topk_acc(logits, target, k=3):
    with torch.no_grad():
        topk = logits.topk(k, dim=1).indices
        return (topk == target.unsqueeze(1)).any(dim=1).float().mean().item()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--data", default=os.path.join("data", "fishid", "raw"))
    parser.add_argument("--epochs", type=int, default=14)
    parser.add_argument("--batch", type=int, default=32)
    parser.add_argument("--lr", type=float, default=3e-4)
    parser.add_argument("--img-size", type=int, default=224)
    parser.add_argument("--arch", default="mobilenet_v3_small",
                        choices=["mobilenet_v3_small", "mobilenet_v3_large"])
    parser.add_argument("--seed", type=int, default=7)
    parser.add_argument("--out", default=os.path.join("data", "fishid", "runs", "run1"))
    parser.add_argument("--workers", type=int, default=0)
    args = parser.parse_args()

    torch.manual_seed(args.seed)
    torch.set_num_threads(max(1, min(os.cpu_count() or 4, 6)))
    torch.backends.cudnn.deterministic = True

    ds = datasets.ImageFolder(args.data, transform=train_tf)
    if len(ds.classes) < 2:
        sys.exit(f"мало классов: {len(ds.classes)}")
    classes = ds.classes
    print(f"classes ({len(classes)}): {classes}")
    print(f"images: {len(ds)}")
    os.makedirs(args.out, exist_ok=True)
    # порядок классов, в котором модель обучена (индексы головы)
    with open(os.path.join(args.out, "class_order.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(classes))

    tr_idx, va_idx, te_idx = split_indices(ds, seed=args.seed)
    # прогон датасета с eval-трансформациями для val/test
    full_eval = datasets.ImageFolder(args.data, transform=eval_tf)
    train_ds = Subset(ds, tr_idx)
    val_ds = Subset(full_eval, va_idx)
    test_ds = Subset(full_eval, te_idx)
    print(f"train={len(train_ds)} val={len(val_ds)} test={len(test_ds)}")

    workers = args.workers if os.name == "nt" else min(args.workers, 4)
    pin = torch.cuda.is_available()
    train_loader = DataLoader(train_ds, batch_size=args.batch, shuffle=True, num_workers=workers, pin_memory=pin)
    val_loader = DataLoader(val_ds, batch_size=args.batch, shuffle=False, num_workers=0, pin_memory=False)
    test_loader = DataLoader(test_ds, batch_size=args.batch, shuffle=False, num_workers=0, pin_memory=False)

    model = models.__dict__[args.arch](weights="IMAGENET1K_V1")
    in_feats = model.classifier[3].in_features
    model.classifier[3] = nn.Linear(in_feats, len(classes))
    device = "cuda" if torch.cuda.is_available() else "cpu"
    model.to(device)

    opt = torch.optim.AdamW(model.parameters(), lr=args.lr, weight_decay=1e-4)
    scheduler = torch.optim.lr_scheduler.CosineAnnealingLR(opt, T_max=args.epochs)
    loss_fn = nn.CrossEntropyLoss()

    os.makedirs(args.out, exist_ok=True)
    best_val = 0.0
    history = []
    t0 = time.time()
    for epoch in range(1, args.epochs + 1):
        model.train()
        run_loss, run_n = 0.0, 0
        for x, y in train_loader:
            x, y = x.to(device), y.to(device)
            opt.zero_grad()
            logits = model(x)
            loss = loss_fn(logits, y)
            loss.backward()
            opt.step()
            run_loss += loss.item() * x.size(0)
            run_n += x.size(0)
        scheduler.step()

        model.eval()
        with torch.no_grad():
            accs, n = 0.0, 0
            for x, y in val_loader:
                x, y = x.to(device), y.to(device)
                accs += topk_acc(model(x), y, 1) * x.size(0)
                n += x.size(0)
            val_acc = accs / n
        print(f"epoch {epoch:02d}/{args.epochs} loss={run_loss / run_n:.4f} val_top1={val_acc:.4f} "
              f"elapsed={time.time() - t0:.0f}s", flush=True)
        history.append({"epoch": epoch, "loss": run_loss / run_n, "val_top1": val_acc})
        if val_acc > best_val:
            best_val = val_acc
            torch.save(model.state_dict(), os.path.join(args.out, "best.pt"))

    model.load_state_dict(torch.load(os.path.join(args.out, "best.pt"), weights_only=True, map_location="cpu"))
    model.eval()
    with torch.no_grad():
        t1 = t3 = n = 0.0
        for x, y in test_loader:
            x, y = x.to(device), y.to(device)
            t1 += topk_acc(model(x), y, 1) * x.size(0)
            t3 += topk_acc(model(x), y, 3) * x.size(0)
            n += x.size(0)
    print(f"TEST top1={t1 / n:.4f} top3={t3 / n:.4f}")

    with open(os.path.join(args.out, "train_summary.json"), "w", encoding="utf-8") as f:
        json.dump({
            "classes": classes,
            "images": {c: ds.class_to_idx[c] for c in classes},
            "splits": {"train": len(tr_idx), "val": len(va_idx), "test": len(te_idx)},
            "test_top1": t1 / n,
            "test_top3": t3 / n,
            "best_val_top1": best_val,
            "epochs": args.epochs,
            "lr": args.lr,
            "batch": args.batch,
            "seed": args.seed,
            "history": history,
        }, f, ensure_ascii=False, indent=2)
    print("saved:", os.path.join(args.out, "best.pt"))


if __name__ == "__main__":
    main()
