using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Refactor.Main
{
    public enum SceneType
    {
        FoundationTest = 0,
        Title = 1,
        Town = 2,
        Dungeon = 3
    }

    // IZ SceneLoader의 사전 검사/오류 경계를 유지한다. 현재는 씬별 MainFlow와 Single 로딩을 쓴다.
    public sealed class SceneLoader
    {
        public string LastError { get; private set; }

        public static string SceneName(SceneType scene)
        {
            return scene switch
            {
                SceneType.Title => "1_Title",
                SceneType.Town => "2_Town",
                SceneType.Dungeon => "3_Dungeon",
                SceneType.FoundationTest => "2_Test",
                _ => throw new ArgumentOutOfRangeException(nameof(scene))
            };
        }

        public bool CanMove(SceneType target)
        {
            LastError = null;
            if (target != SceneType.Title && target != SceneType.Town && target != SceneType.Dungeon)
            {
                LastError = "Unsupported scene destination: " + target;
                return false;
            }
            if (!Application.CanStreamedLevelBeLoaded(SceneName(target)))
            {
                LastError = "Scene is missing from Build Settings: " + SceneName(target);
                return false;
            }
            return true;
        }

        public AsyncOperation BeginLoad(SceneType target)
        {
            if (!CanMove(target))
            {
                return null;
            }
            try
            {
                var operation = SceneManager.LoadSceneAsync(SceneName(target), LoadSceneMode.Single);
                if (operation == null)
                {
                    LastError = "Scene loading did not start: " + SceneName(target);
                }
                return operation;
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                return null;
            }
        }
    }
}
