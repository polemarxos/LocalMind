Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports Microsoft.ML.OnnxRuntime
Imports Microsoft.ML.OnnxRuntime.Tensors

''' <summary>
''' Small helper models run with ONNX Runtime on the CPU: background removal (BiRefNet lite 512, MIT) and
''' depth maps for "copy the layout" (Depth Anything V2 small, Apache-2.0).
''' </summary>
Public Module OnnxTools
    Private ReadOnly Mean As Single() = {0.485F, 0.456F, 0.406F}
    Private ReadOnly Std As Single() = {0.229F, 0.224F, 0.225F}

    Private ReadOnly DepthFiles As MediaFile() = {
        New MediaFile With {.Repo = "onnx-community/depth-anything-v2-small-ONNX", .Path = "onnx/model.onnx", .Size = 127382},
        New MediaFile With {.Repo = "onnx-community/depth-anything-v2-small-ONNX", .Path = "onnx/model.onnx_data", .Size = 98798080}}

    ''' <summary>Connects these tools to the studio.</summary>
    Public Sub Register()
        MediaStudio.BackgroundRemover = AddressOf RemoveBackgroundAsync
        MediaStudio.DepthEstimator = AddressOf DepthMapAsync
        MediaStudio.EdgeDetector = AddressOf ImageTools.SaveEdges
    End Sub

    Private Function Options() As SessionOptions
        Dim o As New SessionOptions()
        o.EnableCpuMemArena = False         ' keeps RAM use low for a one-off run
        o.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount - 1)
        Return o
    End Function

    Public Function RemoveBackgroundAsync(modelFile As String, picture As String, output As String) As Task
        Return Task.Run(Sub()
                            Const S = 512
                            Dim t = ImageTools.ToTensor(picture, S, S, Mean, Std)
                            Using opts = Options()
                                Using session As New InferenceSession(modelFile, opts)
                                    Dim input = New DenseTensor(Of Single)(t.Data, {1, 3, S, S})
                                    Using results = session.Run({NamedOnnxValue.CreateFromTensor(session.InputMetadata.Keys.First(), input)})
                                        Dim o = results.First().AsEnumerable(Of Single)().ToArray()
                                        Dim alpha = o.Select(Function(x) CByte(Math.Clamp(255.0 / (1.0 + Math.Exp(-x)), 0, 255))).ToArray()
                                        ImageTools.ApplyAlpha(picture, alpha, S, S, output)
                                    End Using
                                End Using
                            End Using
                        End Sub)
    End Function

    Public Async Function DepthMapAsync(picture As String, output As String, progress As Action(Of Integer, String), ct As CancellationToken) As Task
        Dim missing = DepthFiles.Where(Function(f) Not MediaStudio.IsPresent(f)).ToList()
        For Each f In missing
            Dim url = $"https://huggingface.co/{f.Repo}/resolve/main/{f.Path}"
            Await Downloader.DownloadFileAsync(url, MediaStudio.LocalPath(f), Nothing,
                Sub(done, size, speed) progress?.Invoke(0, $"Depth model {Downloader.FormatBytes(done)} / {Downloader.FormatBytes(f.Size)}"), ct, f.Size)
        Next
        Await Task.Run(Sub()
                           Const S = 518                              ' multiple of 14, as the model expects
                           Dim t = ImageTools.ToTensor(picture, S, S, Mean, Std)
                           Using opts = Options()
                               Using session As New InferenceSession(MediaStudio.LocalPath(DepthFiles(0)), opts)
                                   Dim input = New DenseTensor(Of Single)(t.Data, {1, 3, S, S})
                                   Using results = session.Run({NamedOnnxValue.CreateFromTensor("pixel_values", input)})
                                       Dim d = results.First().AsEnumerable(Of Single)().ToArray()
                                       ImageTools.SaveGrey(d, S, S, t.OrigW, t.OrigH, output)
                                   End Using
                               End Using
                           End Using
                       End Sub, ct)
    End Function
End Module
