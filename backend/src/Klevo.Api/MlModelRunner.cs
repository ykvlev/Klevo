using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Klevo.Api;

/// <summary>
/// Загружает model.onnx (TreeEnsembleClassifier из onnx_export.py) и выполняет
/// инференс: float[24] -> вероятность улова (0..1).
/// </summary>
public sealed class MlModelRunner : IDisposable
{
    private readonly InferenceSession? _session;

    public MlModelRunner(IConfiguration config)
    {
        var path = config["ML:ModelPath"];
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "models", "model.onnx");
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            _session = new InferenceSession(path);
        }
    }

    public bool IsAvailable => _session is not null;

    public double Predict(float[] features)
    {
        ArgumentNullException.ThrowIfNull(_session);
        var tensor = new DenseTensor<float>(features, [1, features.Length]);
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", tensor),
        };
        using var results = _session.Run(inputs, ["label", "probabilities"]);

        var probs = results[1].AsTensor<float>();
        return probs[0, 1];
    }

    public void Dispose() => _session?.Dispose();
}
