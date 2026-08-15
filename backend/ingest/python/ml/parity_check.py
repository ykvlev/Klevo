import sys
from pathlib import Path

import numpy as np
import onnxruntime as ort
import pandas as pd

sys.path.insert(0, str(Path(__file__).parent))

from features import build_features, load_dotenv
from train import MODEL_COLS

SPOT = sys.argv[1] if len(sys.argv) > 1 else "a1111111-0000-4000-8000-000000000003"
DATE = sys.argv[2] if len(sys.argv) > 2 else "2026-08-15"
D0, D1 = "2023-01-01", DATE

load_dotenv()
df = build_features(D0, D1, [SPOT])
row = df[df["date"] == pd.Timestamp(DATE)].iloc[0]
vec = np.array(
    [row[c] if pd.notna(row[c]) else np.nan for c in MODEL_COLS],
    dtype=np.float32,
)
sess = ort.InferenceSession(str(Path(__file__).parent / "models" / "model.onnx"))
out = sess.run(None, {"input": vec[None, :]})
prob = float(out[1][0][1])
print(f"prob={prob:.6f} score={round(prob * 100)}")
print("VEC_BEGIN")
for c, v in zip(MODEL_COLS, vec):
    print(f"{c}\t{float(v):.4f}" if not np.isnan(v) else f"{c}\tNaN")
print("VEC_END")
