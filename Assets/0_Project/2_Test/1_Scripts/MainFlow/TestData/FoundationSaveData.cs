using System;
using System.IO;
using Refactor.Main;
using Refactor.Util;

namespace Refactor
{
    public enum FoundationLoadResult
    {
        Loaded,
        Missing,
        Corrupt,
        UnsupportedVersion,
        IOError
    }

    [Serializable]
    public sealed class FoundationSaveData
    {
        public string Format;
        public int Version;
        public int Counter;
        public int ProcessedRequestCount;
        public string Label;
    }

    // DTO와 호환성 정책을 소유한다. 파일 교체는 공통 SaveLoadHelper의 책임이다.
    public static class FoundationSave
    {
        public const string CurrentFormat = "P2.Foundation";
        public const int CurrentVersion = 1;

        public static void Save(string path, IFoundationDataReader state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            var dto = new FoundationSaveData
            {
                Format = CurrentFormat,
                Version = CurrentVersion,
                Counter = state.Counter,
                ProcessedRequestCount = state.ProcessedRequestCount,
                Label = state.Label
            };
            SaveLoadHelper.SaveJson(path, dto);
        }

        // 결과가 Loaded일 때만 호출자가 현재 상태에 반영한다. 실패한 파일을 덮어쓰지 않는다.
        public static FoundationLoadResult Load(string path, GameSystemSheet settings, out FoundationSaveData dto)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }
            dto = null;
            try
            {
                // JsonUtility는 없는 필드를 기본값으로 채운다. Overwrite의 초기값으로 누락을 구분한다.
                var parsed = new FoundationSaveData
                {
                    Version = int.MinValue,
                    Counter = int.MinValue,
                    ProcessedRequestCount = int.MinValue
                };
                SaveLoadHelper.LoadJsonInto(path, parsed);
                if (parsed.Format != CurrentFormat || parsed.Version < 1)
                {
                    return FoundationLoadResult.Corrupt;
                }
                if (parsed.Version != CurrentVersion)
                {
                    return FoundationLoadResult.UnsupportedVersion;
                }
                if (parsed.Counter < -settings.CounterLimit || parsed.Counter > settings.CounterLimit
                    || parsed.ProcessedRequestCount < 0 || string.IsNullOrWhiteSpace(parsed.Label)
                    || parsed.Label.Length > settings.LabelMaxLength || parsed.Label != parsed.Label.Trim())
                {
                    return FoundationLoadResult.Corrupt;
                }
                dto = parsed;
                return FoundationLoadResult.Loaded;
            }
            catch (FileNotFoundException)
            {
                return FoundationLoadResult.Missing;
            }
            catch (DirectoryNotFoundException)
            {
                return FoundationLoadResult.Missing;
            }
            catch (ArgumentException)
            {
                return FoundationLoadResult.Corrupt;
            }
            catch (IOException)
            {
                return FoundationLoadResult.IOError;
            }
            catch (UnauthorizedAccessException)
            {
                return FoundationLoadResult.IOError;
            }
        }
    }
}
