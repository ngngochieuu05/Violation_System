namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.ML.Inference;

public class YoloModelOptions
{
    public const string SectionName = "YoloModel";

    public string PythonExecutable { get; set; } = "python";
    public string InferenceScriptPath { get; set; } = "ML/scripts/run_yolo_inference.py";
    public string WorkerScriptPath { get; set; } = "ML/scripts/yolo_worker.py";
    public string ModelPath { get; set; } = "ML/weights/best.pt";
    public string SampleSourcePath { get; set; } = "ML/samples/sample.jpg";
    public string DeviceMode { get; set; } = "auto";
    public bool UseHalfPrecision { get; set; } = true;
    public int ImageSize { get; set; } = 640;
    public int MaxFrames { get; set; } = 8;
    public int WorkerStartupTimeoutSeconds { get; set; } = 30;
}
