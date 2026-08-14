#!/usr/bin/env python3
"""Экспорт обученного LightGBM (бинарная классификация) в ONNX
без внешних конвертеров (skl2onnx/hummingbird не имеют wheels для Py3.14).

Строит TreeEnsembleClassifier: листья -> class_weights (log-odds),
post_transform=LOGISTIC (P1 = sigmoid(score1 - score0), score0=0).

    from onnx_export import lgbm_to_onnx
    lgbm_to_onnx(model, "model.onnx", ["f1", "f2"])
"""
from __future__ import annotations

import numpy as np
import onnx
from onnx import TensorProto, helper, numpy_helper


def lgbm_to_onnx(model, path: str, feature_names: list[str]) -> None:
    booster = model.booster_
    dump = booster.dump_model()
    trees = dump["tree_info"]
    tree_ids, node_ids, feature_ids = [], [], []
    modes, values, true_ids, false_ids, missing_true = [], [], [], [], []
    class_tree_ids, class_node_ids, class_class_ids, class_weights = (
        [], [], [], [])

    max_int = 2**63 - 1
    for t, tree in enumerate(trees):
        counter = 0
        nodes_by_id: dict[int, tuple] = {}

        def walk(n: dict) -> int:
            nonlocal counter
            node_id = counter
            counter += 1
            if "leaf_index" in n:
                nodes_by_id[node_id] = ("leaf", float(n["leaf_value"]))
            else:
                left = walk(n["left_child"])
                right = walk(n["right_child"])
                nodes_by_id[node_id] = (
                    "split", int(n["split_feature"]), float(n["threshold"]),
                    left, right, bool(n.get("default_left", False)))
            return node_id

        walk(tree["tree_structure"])
        for node_id in sorted(nodes_by_id):
            info = nodes_by_id[node_id]
            if info[0] == "leaf":
                tree_ids.append(t)
                node_ids.append(node_id)
                feature_ids.append(0)
                modes.append("LEAF")
                values.append(0.0)
                true_ids.append(node_id)
                false_ids.append(node_id)
                missing_true.append(0)
                class_tree_ids.append(t)
                class_node_ids.append(node_id)
                class_class_ids.append(1)
                class_weights.append(info[1])
            else:
                _, feat, thr, left, right, default_left = info
                tree_ids.append(t)
                node_ids.append(node_id)
                feature_ids.append(feat)
                modes.append("BRANCH_LEQ")
                values.append(thr)
                true_ids.append(left)
                false_ids.append(right)
                missing_true.append(1 if default_left else 0)

    n_trees = len(trees)
    tree_ensemble = helper.make_node(
        "TreeEnsembleClassifier",
        inputs=["input"],
        outputs=["label", "probabilities"],
        domain="ai.onnx.ml",
        nodes_treeids=tree_ids,
        nodes_nodeids=node_ids,
        nodes_featureids=feature_ids,
        nodes_modes=modes,
        nodes_values=values,
        nodes_truenodeids=true_ids,
        nodes_falsenodeids=false_ids,
        nodes_missing_value_tracks_true=missing_true,
        class_treeids=class_tree_ids,
        class_nodeids=class_node_ids,
        class_ids=class_class_ids,
        class_weights=class_weights,
        classlabels_int64s=[0, 1],
        post_transform="LOGISTIC",
    )

    graph = helper.make_graph(
        [tree_ensemble],
        "klevo_lgbm",
        [helper.make_tensor_value_info("input", TensorProto.FLOAT,
                                       [None, len(feature_names)])],
        [helper.make_tensor_value_info("label", TensorProto.INT64, [None, 1]),
         helper.make_tensor_value_info("probabilities", TensorProto.FLOAT,
                                       [None, 2])],
    )
    model_onnx = helper.make_model(graph, opset_imports=[
        helper.make_opsetid("", 18),
        helper.make_opsetid("ai.onnx.ml", 3)],
        ir_version=onnx.IR_VERSION)
    onnx.checker.check_model(model_onnx)
    with open(path, "wb") as f:
        f.write(model_onnx.SerializeToString())
