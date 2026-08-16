using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Klevo.Api;

/// <summary>Загрузка модели fishid (ONNX) и инференс: изображение -> вероятности по видам.</summary>
public sealed class FishIdService : IDisposable
{
    public const int InputSize = 224;
    public const string ModelVersion = "fishid-v1-mobilenet-v3-small";

    private readonly InferenceSession? _session;
    private readonly string[] _classLatin;
    private readonly string[] _classRu;

    public FishIdService(IConfiguration config)
    {
        var modelPath = config["FishId:ModelPath"];
        if (string.IsNullOrWhiteSpace(modelPath))
            modelPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "models", "fishid", "model.onnx");
        var classesPath = config["FishId:ClassesPath"];
        if (string.IsNullOrWhiteSpace(classesPath))
            classesPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "models", "fishid", "classes.txt");

        if (File.Exists(modelPath) && File.Exists(classesPath))
        {
            _session = new InferenceSession(modelPath);
            var lines = File.ReadAllLines(classesPath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();
            _classLatin = new string[lines.Length];
            _classRu = new string[lines.Length];
            for (var i = 0; i < lines.Length; i++)
            {
                var parts = lines[i].Split('|');
                _classLatin[i] = parts.Length > 1 ? parts[1] : lines[i];
                _classRu[i] = parts.Length > 2 ? parts[2] : _classLatin[i];
            }
        }
        else
        {
            _classLatin = [];
            _classRu = [];
        }
    }

    public bool IsAvailable => _session is not null;

    public IReadOnlyList<FishIdPrediction> Predict(byte[] imageBytes, int topK = 3)
    {
        ArgumentNullException.ThrowIfNull(_session);
        var input = Preprocess(imageBytes);
        var tensor = new DenseTensor<float>(input, [1, InputSize, InputSize, 3]);
        using var results = _session.Run(
            [NamedOnnxValue.CreateFromTensor("image", tensor)], ["logits"]);

        var logits = results[0].AsTensor<float>();
        var logitsArr = logits.ToArray();
        var probs = Softmax(logitsArr);

        var order = Enumerable.Range(0, _classLatin.Length)
            .OrderByDescending(i => probs[i])
            .Take(topK)
            .Select(i => new FishIdPrediction(
                _classLatin[i], _classRu[i], probs[i]))
            .ToList();
        return order;
    }

    public void Dispose() => _session?.Dispose();

    private static float[] Softmax(float[] logits)
    {
        var n = logits.Length;
        var probs = new float[n];
        var max = float.MinValue;
        for (var i = 0; i < n; i++)
            max = Math.Max(max, logits[i]);
        var sum = 0.0;
        for (var i = 0; i < n; i++)
        {
            probs[i] = MathF.Exp(logits[i] - max);
            sum += probs[i];
        }
        for (var i = 0; i < n; i++)
            probs[i] = (float)(probs[i] / sum);
        return probs;
    }

    /// <summary>Повторяет torchvision eval: resize короткой стороны до 256, затем центральный кроп 224×224.</summary>
    private static float[] Preprocess(byte[] imageBytes)
    {
        using var image = Image.Load<Rgba32>(imageBytes);
        if (image.Width < 10 || image.Height < 10)
            throw new InvalidDataException("Изображение слишком маленькое");

        // resize короткой стороны до 256 (сохраняя пропорции)
        var scale = 256.0 / Math.Min(image.Width, image.Height);
        var w = (int)Math.Round(image.Width * scale);
        var h = (int)Math.Round(image.Height * scale);
        using var resized = image.Clone(ctx => ctx.Resize(w, h));

        var x0 = (w - InputSize) / 2;
        var y0 = (h - InputSize) / 2;

        var pixels = new float[InputSize * InputSize * 3];
        var idx = 0;
        resized.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < InputSize; y++)
            {
                var row = accessor.GetRowSpan(y0 + y);
                for (var x = 0; x < InputSize; x++)
                {
                    var p = row[x0 + x];
                    pixels[idx++] = p.R;
                    pixels[idx++] = p.G;
                    pixels[idx++] = p.B;
                }
            }
        });
        return pixels;
    }
}

public readonly record struct FishIdPrediction(string NameLatin, string NameRu, float Confidence);

public record FishIdDataUrlRequest(string? DataUrl);
