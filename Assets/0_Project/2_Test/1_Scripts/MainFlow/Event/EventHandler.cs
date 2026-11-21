using Refactor.Util;

namespace Refactor.Main.Event
{
    public interface IEventHandler
    {
    }

    // DS/IZ와 같이 보유한 구체 Handler에서 요청 타입을 찾는다.
    public sealed class EventHandler
    {
        public readonly FoundationEventHandler Foundation;
        public readonly TownEventHandler Town;
        public readonly DungeonEventHandler Dungeon;
        public readonly CharacterEventHandler Character;
        public readonly QuestEventHandler Quest;
        public readonly HeroMenuEventHandler HeroMenu;

        public EventHandler(FoundationEventHandler foundation)
        {
            Foundation = foundation;
            DI.PubSub(Foundation);
        }

        public EventHandler(TownEventHandler town, CharacterEventHandler character = null, QuestEventHandler quest = null,
            HeroMenuEventHandler heroMenu = null)
        {
            Town = town;
            DI.PubSub(Town);
            Character = character;
            Quest = quest;
            HeroMenu = heroMenu;
        }

        public EventHandler(DungeonEventHandler dungeon, CharacterEventHandler character = null, HeroMenuEventHandler heroMenu = null)
        {
            Dungeon = dungeon;
            DI.PubSub(Dungeon);
            Character = character;
            HeroMenu = heroMenu;
        }
    }
}
