namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.ML.Inference;

public class YoloModelOptions
{
    public const string SectionName = "YoloModel";

    public string PythonExecutable { get; set; } = "python";
    public string InferenceScriptPath { get; set; } = "ML/scripts/run_yolo_inference.py";
    public string ModelPath { get; set; } = "ML/weights/best.pt";
    public string SampleSourcePath { get; set; } = "ML/samples/sample.jpg";
}
