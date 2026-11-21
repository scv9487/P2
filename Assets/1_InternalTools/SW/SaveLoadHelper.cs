using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Refactor
{
    public static class SaveLoadHelper
    {
        private const string ReplaceBackupSuffix = ".replacebak_";
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        // DTO의 형식/버전 호환성 판단은 실제 게임 저장 데이터 계층에서 처리한다.
        public static void SaveJson<T>(string path, T data) where T : class
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            WriteJsonFileSafely<T>(path, JsonUtility.ToJson(data, true));
        }

        public static T LoadJson<T>(string path) where T : class
        {
            return JsonUtility.FromJson<T>(File.ReadAllText(path, Utf8NoBom));
        }

        // 누락된 필드를 구분할 초기값은 저장 형식 계층에서 제공한다.
        public static void LoadJsonInto<T>(string path, T data, Action<string> validateJson = null) where T : class
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            string json = File.ReadAllText(path, Utf8NoBom);
            validateJson?.Invoke(json);
            JsonUtility.FromJsonOverwrite(json, data);
        }

        private static void EnsureDirectoryExists(string directoryPath)
        {
            if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }
        // [8단계 파일 처리] 임시 파일에 쓰기 → 파싱 확인 → 원본 교체. 예제 DTO의 버전 정책은 FoundationSave가 담당한다.
        private static void WriteJsonFileSafely<T>(string path, string contents) where T : class
        {
            string tempPath = path + ".tmp";
            EnsureDirectoryExists(Path.GetDirectoryName(path));

            try
            {
                WriteUtf8NoBomText(tempPath, contents);
                ValidateJsonFile<T>(tempPath);
                CommitTemporaryFile(tempPath, path);
            }
            catch
            {
                TryDeleteTemporaryFile(tempPath);
                throw;
            }
        }

        private static void WriteUtf8NoBomText(string path, string contents)
        {
            using FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using StreamWriter writer = new StreamWriter(stream, Utf8NoBom);
            writer.Write(contents);
        }

        private static void ValidateJsonFile<T>(string path) where T : class
        {
            string json = File.ReadAllText(path, Utf8NoBom);
            if (JsonUtility.FromJson<T>(json) == null)
            {
                throw new FormatException("Invalid save JSON");
            }
        }

        private static void CommitTemporaryFile(string tempPath, string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tempPath, path, null);
                }
                catch (Exception replaceException)
                {
                    CommitTemporaryFileWithMoveFallback(tempPath, path, replaceException);
                }

                return;
            }

            File.Move(tempPath, path);
        }

        private static void CommitTemporaryFileWithMoveFallback(string tempPath, string path, Exception replaceException)
        {
            string backupPath = CreateReplaceBackupPath(path);
            bool originalMovedToBackup = false;

            Debug.LogWarning($"File.Replace 실패로 이동 기반 저장 fallback을 시도합니다: {path}. 오류: {replaceException.Message}");

            try
            {
                if (!File.Exists(path))
                {
                    File.Move(tempPath, path);
                    return;
                }

                File.Move(path, backupPath);
                originalMovedToBackup = true;
                File.Move(tempPath, path);
                TryDeleteReplaceBackupFile(backupPath);
            }
            catch (Exception fallbackException)
            {
                TryRestoreReplaceBackup(
                    backupPath,
                    path,
                    originalMovedToBackup);

                throw new IOException(
                    $"저장 파일 교체 fallback 실패: {path}. File.Replace 오류: {replaceException.Message}",
                    fallbackException);
            }
        }

        private static string CreateReplaceBackupPath(string path)
        {
            return path + ReplaceBackupSuffix + Guid.NewGuid().ToString("N");
        }

        private static void TryRestoreReplaceBackup(string backupPath, string path, bool originalMovedToBackup)
        {
            if (!originalMovedToBackup || File.Exists(path) || !File.Exists(backupPath))
            {
                return;
            }

            try
            {
                File.Move(backupPath, path);
            }
            catch (Exception restoreException)
            {
                Debug.LogError($"저장 파일 fallback 복원 실패: {path}. 백업: {backupPath}. 오류: {restoreException.Message}");
            }
        }

        private static void TryDeleteTemporaryFile(string tempPath)
        {
            TryDeleteFile(tempPath, "임시 저장 파일");
        }

        private static void TryDeleteReplaceBackupFile(string backupPath)
        {
            TryDeleteFile(backupPath, "교체 백업 파일");
        }

        private static void TryDeleteFile(string path, string label)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception deleteException)
            {
                Debug.LogWarning($"{label} 삭제 실패: {path}. 오류: {deleteException.Message}");
            }
        }



    }
}
