"""Regenerate the offline test fixture with Python and onnx==1.20.1.

The graph produces [token_id, 1] for each input position. Tests can independently
calculate mean-pooled, normalized outputs and detect inclusion of padded tokens.
"""

from pathlib import Path

import onnx
from onnx import TensorProto, helper


def generate() -> None:
    axes = helper.make_tensor("axes", TensorProto.INT64, [1], [2])
    one = helper.make_tensor("one", TensorProto.FLOAT, [1], [1.0])
    graph = helper.make_graph(
        [
            helper.make_node("Cast", ["input_ids"], ["ids"], to=TensorProto.FLOAT),
            helper.make_node("Shape", ["input_ids"], ["shape"]),
            helper.make_node("ConstantOfShape", ["shape"], ["ones"], value=one),
            helper.make_node("Unsqueeze", ["ids", "axes"], ["id_column"]),
            helper.make_node("Unsqueeze", ["ones", "axes"], ["one_column"]),
            helper.make_node("Concat", ["id_column", "one_column"], ["last_hidden_state"], axis=2),
        ],
        "tokenizer-embedding",
        [
            helper.make_tensor_value_info("input_ids", TensorProto.INT64, ["batch", "sequence"]),
            helper.make_tensor_value_info("attention_mask", TensorProto.INT64, ["batch", "sequence"]),
        ],
        [helper.make_tensor_value_info("last_hidden_state", TensorProto.FLOAT, ["batch", "sequence", 2])],
        initializer=[axes],
    )
    model = helper.make_model(
        graph,
        producer_name="SemanticStart.Tests",
        ir_version=9,
        opset_imports=[helper.make_opsetid("", 13)],
    )
    onnx.checker.check_model(model)
    onnx.save(model, Path(__file__).with_name("tokenizer-embedding.onnx"))


if __name__ == "__main__":
    generate()
