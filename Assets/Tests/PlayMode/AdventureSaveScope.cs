using System;
using System.IO;
using Refactor.Main;

namespace P2.Tests
{
    // 씬 Awake보다 먼저 생성하고 씬 언로드 뒤 해제한다. 실제 사용자 저장 경로에는 접근하지 않는다.
    internal sealed class AdventureSaveScope : IDisposable
    {
        private readonly string _previousOverride;
        public string DirectoryPath { get; }
        public string SavePath { get; }

        public AdventureSaveScope()
        {
            _previousOverride = MainFlow.AdventureSavePathOverride;
            DirectoryPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "P2-AdventureTests-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(DirectoryPath);
            SavePath = Path.Combine(DirectoryPath, "adventure.json");
            MainFlow.AdventureSavePathOverride = SavePath;
        }

        public void Dispose()
        {
            MainFlow.AdventureSavePathOverride = _previousOverride;
            string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string parent = Path.GetDirectoryName(Path.GetFullPath(DirectoryPath));
            if (!string.Equals(parent, tempRoot, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(DirectoryPath).StartsWith("P2-AdventureTests-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("테스트가 소유한 임시 폴더만 정리할 수 있습니다.");
            }
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, true);
            }
        }
    }
}
