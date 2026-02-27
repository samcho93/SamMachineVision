using System.IO.Compression;

namespace MVXTester.Core.Serialization;

/// <summary>
/// ZIP 기반 프로젝트 아카이브 (.mvxp) 읽기/쓰기.
/// 아카이브 구조:
///   graph.json          ← 직렬화된 그래프
///   assets/             ← 참조 파일 번들
///     image1.png
///     cascade.xml
///     video1.mp4
/// </summary>
public static class ProjectArchive
{
    public const string Extension = ".mvxp";
    public const string GraphFileName = "graph.json";
    public const string AssetsFolder = "assets";

    /// <summary>
    /// 프로젝트를 ZIP 아카이브로 저장.
    /// graphJson 내의 절대 경로를 상대 경로로 치환하고,
    /// 참조 파일을 assets/ 폴더에 복사하여 ZIP으로 묶음.
    /// </summary>
    /// <param name="archivePath">저장할 .mvxp 파일 경로</param>
    /// <param name="graphJson">직렬화된 그래프 JSON 문자열</param>
    /// <param name="filePathMap">원본 절대 경로 → assets/내 상대 경로 매핑</param>
    public static void Save(string archivePath, string graphJson,
        Dictionary<string, string> filePathMap)
    {
        // 기존 파일이 있으면 삭제 (ZipFile.Open with Create는 기존 파일 위에 쓸 수 없음)
        if (File.Exists(archivePath))
            File.Delete(archivePath);

        // graphJson 내의 절대 경로를 상대 경로로 치환
        var modifiedJson = graphJson;
        foreach (var (absolutePath, relativePath) in filePathMap)
        {
            // JSON 내에서 경로는 이스케이프된 형태일 수 있음
            // 예: "D:\\Work\\image.png" → "assets/image.png"
            var escapedAbsolute = absolutePath.Replace("\\", "\\\\");
            var escapedRelative = relativePath.Replace("/", "\\/");
            modifiedJson = modifiedJson.Replace(escapedAbsolute, escapedRelative);

            // 또한 forward slash 형태도 처리
            var forwardSlashAbsolute = absolutePath.Replace("\\", "/");
            modifiedJson = modifiedJson.Replace(forwardSlashAbsolute, relativePath);
        }

        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);

        // graph.json 기록
        var graphEntry = archive.CreateEntry(GraphFileName, CompressionLevel.Optimal);
        using (var writer = new StreamWriter(graphEntry.Open()))
        {
            writer.Write(modifiedJson);
        }

        // assets/ 폴더에 참조 파일 복사
        foreach (var (absolutePath, relativePath) in filePathMap)
        {
            if (!File.Exists(absolutePath)) continue;

            var assetEntry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
            using var entryStream = assetEntry.Open();
            using var fileStream = File.OpenRead(absolutePath);
            fileStream.CopyTo(entryStream);
        }
    }

    /// <summary>
    /// ZIP 아카이브에서 프로젝트 로드.
    /// 임시 디렉토리에 추출하고, graph.json 내의 상대 경로를
    /// 임시 디렉토리 기준 절대 경로로 변환.
    /// </summary>
    /// <param name="archivePath">.mvxp 파일 경로</param>
    /// <returns>(graphJson: 경로 변환된 JSON, extractDir: 임시 추출 디렉토리)</returns>
    public static (string graphJson, string extractDir) Load(string archivePath)
    {
        // 임시 디렉토리 생성
        var extractDir = Path.Combine(Path.GetTempPath(), $"MVXTester_{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractDir);

        // ZIP 추출
        ZipFile.ExtractToDirectory(archivePath, extractDir);

        // graph.json 읽기
        var graphPath = Path.Combine(extractDir, GraphFileName);
        if (!File.Exists(graphPath))
            throw new FileNotFoundException("graph.json not found in archive.");

        var graphJson = File.ReadAllText(graphPath);

        // 상대 경로(assets/xxx)를 절대 경로로 변환
        // assets/ 디렉토리 내의 모든 파일을 스캔하여 치환
        var assetsDir = Path.Combine(extractDir, AssetsFolder);
        if (Directory.Exists(assetsDir))
        {
            var assetFiles = Directory.GetFiles(assetsDir, "*", SearchOption.AllDirectories);
            foreach (var assetFile in assetFiles)
            {
                // 상대 경로 계산 (assets/filename.ext)
                var relativePath = Path.GetRelativePath(extractDir, assetFile).Replace("\\", "/");
                // JSON 내에서의 이스케이프된 형태
                var escapedRelative = relativePath.Replace("/", "\\/");
                var absolutePath = assetFile.Replace("\\", "\\\\");

                graphJson = graphJson.Replace(escapedRelative, absolutePath);

                // forward slash 형태도 처리
                graphJson = graphJson.Replace(relativePath, assetFile.Replace("\\", "\\\\"));
            }
        }

        return (graphJson, extractDir);
    }

    /// <summary>
    /// 아카이브가 .mvxp 형식인지 확인
    /// </summary>
    public static bool IsProjectArchive(string filePath)
    {
        return Path.GetExtension(filePath).Equals(Extension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 임시 추출 디렉토리 정리
    /// </summary>
    public static void CleanupExtractDir(string? extractDir)
    {
        if (string.IsNullOrEmpty(extractDir) || !Directory.Exists(extractDir))
            return;

        try
        {
            Directory.Delete(extractDir, recursive: true);
        }
        catch
        {
            // 파일이 사용 중일 수 있으므로 무시
        }
    }
}
