using System;
using System.Collections.Generic;
using System.Globalization;
using Refactor.Main;
using Util.Sheet;

namespace Refactor.Util
{
    public enum SkillTreeKind
    {
        Main,
        Sub
    }

    public enum SkillType
    {
        Active,
        Passive
    }

    public enum SkillTargetType
    {
        None,
        SingleEnemy,
        Self,
        SingleAlly,
        AllEnemies,
        AllAllies,
        AllUnits
    }

    public enum SkillAttackType
    {
        None,
        Melee,
        Ranged
    }

    public sealed class SkillDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string DescriptionFormat { get; }
        public string Description
        {
            get
            {
                var values = new object[Values.Count];
                for (int index = 0; index < values.Length; index++)
                {
                    values[index] = Values[index];
                }
                return string.Format(CultureInfo.InvariantCulture, DescriptionFormat, values);
            }
        }
        public string TreeId { get; }
        public SkillTreeKind TreeKind { get; }
        public SkillType Type => TreeKind == SkillTreeKind.Sub ? SkillType.Passive : SkillType.Active;
        public int Order { get; }
        public int RequiredLevel { get; }
        public int Cost { get; }
        public IReadOnlyList<string> PrerequisiteIds { get; }
        public SkillTargetType TargetType { get; }
        public CombatDamageType DamageType { get; }
        public SkillAttackType AttackType { get; }
        public IReadOnlyList<float> Values { get; }
        public float Cooldown { get; }
        public float EnergyCost { get; }
        public float StartDelay { get; }
        public float EndDelay { get; }
        public float BaseCycle => StartDelay + EndDelay;
        public bool IsDelayAdjustedByAttackSpeed { get; }
        public bool AdjustedByAttackSpeed => IsDelayAdjustedByAttackSpeed;
        public string RequiredEquipment { get; }
        public bool IsBasicAttack { get; }

        internal SkillDefinition(string[] row)
        {
            Id = SkillSheet.Required(row[0]);
            Name = SkillSheet.Required(row[1]);
            DescriptionFormat = SkillSheet.Required(row[2]);
            TreeId = SkillSheet.Required(row[3]);
            TreeKind = SkillSheet.EnumValue<SkillTreeKind>(row[4]);
            Order = SkillSheet.Integer(row[5], 0, 100000);
            RequiredLevel = SkillSheet.Integer(row[6], 0, 10000);
            Cost = SkillSheet.Integer(row[7], 0, 9999);
            PrerequisiteIds = Array.AsReadOnly(SkillSheet.List(row[8]));
            TargetType = SkillSheet.EnumValue<SkillTargetType>(row[9]);
            DamageType = SkillSheet.EnumValue<CombatDamageType>(row[10]);
            AttackType = SkillSheet.EnumValue<SkillAttackType>(row[11]);
            var values = new List<float>();
            foreach (string value in SkillSheet.List(row[12]))
            {
                values.Add(SkillSheet.Number(value, -1000000f, 1000000f));
            }
            Values = values.AsReadOnly();
            Cooldown = SkillSheet.Number(row[13], 0f, 100000f);
            EnergyCost = SkillSheet.Number(row[14], 0f, 100000f);
            StartDelay = SkillSheet.Number(row[15], 0f, 100000f);
            EndDelay = SkillSheet.Number(row[16], 0f, 100000f);
            IsDelayAdjustedByAttackSpeed = SkillSheet.Integer(row[17], 0, 1) == 1;
            RequiredEquipment = row[18];
            IsBasicAttack = SkillSheet.Integer(row[19], 0, 1) == 1;
            _ = Description;
            if (IsBasicAttack && (TreeKind != SkillTreeKind.Main || Cost != 0 || PrerequisiteIds.Count != 0 || BaseCycle <= 0f)
                || Type == SkillType.Passive && (TargetType != SkillTargetType.None || IsBasicAttack))
            {
                throw new FormatException("기본 공격/패시브 스킬 정의가 올바르지 않습니다: " + Id);
            }
        }
    }

    public sealed class SkillWeaponDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string BasicSkillId { get; }

        internal SkillWeaponDefinition(string[] row)
        {
            Id = SkillSheet.Required(row[0]);
            Name = SkillSheet.Required(row[1]);
            BasicSkillId = SkillSheet.Required(row[2]);
        }
    }

    public sealed class SkillClassDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string ActiveTreeId { get; }
        public string WeaponId { get; }
        public string WeaponType => WeaponId;
        public string BasicAttackId { get; }

        internal SkillClassDefinition(string[] row, SkillWeaponDefinition weapon)
        {
            Id = SkillSheet.Required(row[0]);
            Name = SkillSheet.Required(row[1]);
            ActiveTreeId = SkillSheet.Required(row[2]);
            WeaponId = weapon.Id;
            BasicAttackId = weapon.BasicSkillId;
        }
    }

    public sealed class SkillSheet
    {
        private readonly Dictionary<string, SkillDefinition> _skills = new Dictionary<string, SkillDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, SkillClassDefinition> _classes = new Dictionary<string, SkillClassDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, SkillWeaponDefinition> _weapons = new Dictionary<string, SkillWeaponDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _characterClasses = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _settings = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _texts = new Dictionary<string, string>(StringComparer.Ordinal);
        public IReadOnlyList<SkillDefinition> Skills { get; }
        public IReadOnlyList<SkillDefinition> BasicSkills { get; }
        public IReadOnlyList<SkillClassDefinition> Classes { get; }
        public IReadOnlyList<SkillWeaponDefinition> Weapons { get; }
        public int InitialMainSkillPoints => (int)Setting("InitialMainSkillPoints");
        public int InitialSubSkillPoints => (int)Setting("InitialSubSkillPoints");
        public int MainSkillPointsPerLevel => (int)Setting("MainSkillPointsPerLevel");
        public int SubSkillPointsPerLevel => (int)Setting("SubSkillPointsPerLevel");
        public int MaxSkillPoints => (int)Setting("MaxSkillPoints");
        public float GlobalCooldown => Setting("GlobalCooldown");
        public float BaseMaxEnergy => Setting("BaseMaxEnergy");
        public float MaxEnergyBase => BaseMaxEnergy;
        public float MaxEnergyWillPower => Setting("MaxEnergyWillPower");
        public float EnergyRegeneration => Setting("EnergyRegeneration");
        public float EnergyRefreshSeconds => Setting("EnergyRefreshSeconds");
        public float EnergyTickSeconds => EnergyRefreshSeconds;

        public SkillSheet(string skills, string classes, string characterClasses, string weapons, string settings, string texts)
        {
            var learned = new List<SkillDefinition>();
            var basic = new List<SkillDefinition>();
            foreach (var row in CsvTable.Read(skills, "SkillDefinitionTable", "Id", "Name", "Description", "TreeId", "TreeKind", "Order", "RequiredLevel", "Cost", "PrerequisiteIds", "TargetType", "DamageType", "AttackType", "Values", "Cooldown", "EnergyCost", "StartDelay", "EndDelay", "IsDelayAdjustedByAttackSpeed", "RequiredEquipment", "IsBasicAttack"))
            {
                var skill = new SkillDefinition(row);
                if (!_skills.TryAdd(skill.Id, skill))
                {
                    throw new FormatException("스킬 ID 중복: " + skill.Id);
                }
                (skill.IsBasicAttack ? basic : learned).Add(skill);
            }
            foreach (var skill in learned)
            {
                ValidatePrerequisites(skill, new HashSet<string>(StringComparer.Ordinal));
            }
            Skills = learned.AsReadOnly();
            BasicSkills = basic.AsReadOnly();
            var weaponList = new List<SkillWeaponDefinition>();
            foreach (var row in CsvTable.Read(weapons, "SkillWeaponTable", "Id", "Name", "BasicSkillId"))
            {
                var weapon = new SkillWeaponDefinition(row);
                if (!GetSkill(weapon.BasicSkillId).IsBasicAttack || !_weapons.TryAdd(weapon.Id, weapon))
                {
                    throw new FormatException("무기 기본 공격 참조/ID가 올바르지 않습니다: " + weapon.Id);
                }
                weaponList.Add(weapon);
            }
            Weapons = weaponList.AsReadOnly();
            var classList = new List<SkillClassDefinition>();
            var activeTrees = new HashSet<string>(StringComparer.Ordinal);
            var classWeapons = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in CsvTable.Read(classes, "SkillClassTable", "Id", "Name", "ActiveTreeId", "WeaponId"))
            {
                var definition = new SkillClassDefinition(row, GetWeapon(row[3]));
                if (!_classes.TryAdd(definition.Id, definition) || !activeTrees.Add(definition.ActiveTreeId)
                    || !classWeapons.Add(definition.WeaponId))
                {
                    throw new FormatException("직업 ID/액티브 트리/무기 종류 중복: " + definition.Id);
                }
                classList.Add(definition);
            }
            Classes = classList.AsReadOnly();
            foreach (var skill in learned)
            {
                if (skill.TreeKind == SkillTreeKind.Main && !activeTrees.Contains(skill.TreeId))
                {
                    throw new FormatException("스킬의 직업 트리가 없습니다: " + skill.Id);
                }
            }
            foreach (var row in CsvTable.Read(characterClasses, "CharacterClassTable", "CharacterId", "ClassId"))
            {
                GetClass(row[1]);
                if (!_characterClasses.TryAdd(Required(row[0]), row[1]))
                {
                    throw new FormatException("인물의 직업 배정 중복: " + row[0]);
                }
            }
            foreach (var row in CsvTable.Read(settings, "SkillSettingTable", "Key", "Value"))
            {
                if (!_settings.TryAdd(Required(row[0]), Number(row[1], -1000000f, 1000000f)))
                {
                    throw new FormatException("스킬 설정 중복: " + row[0]);
                }
            }
            foreach (string key in new[] { "InitialMainSkillPoints", "InitialSubSkillPoints", "MainSkillPointsPerLevel", "SubSkillPointsPerLevel", "MaxSkillPoints" })
            {
                float value = Setting(key);
                if (value < 0f || value > 9999f || value != (int)value || key == "MaxSkillPoints" && value < 1f)
                {
                    throw new FormatException("SP 설정은 0~9999의 정수여야 합니다: " + key);
                }
            }
            if (InitialMainSkillPoints > MaxSkillPoints || InitialSubSkillPoints > MaxSkillPoints)
            {
                throw new FormatException("초기 SP가 최대 SP를 초과합니다.");
            }
            if (EnergyTickSeconds <= 0f || MaxEnergyBase < 0f || GlobalCooldown < 0f)
            {
                throw new FormatException("기력 갱신 주기는 양수, 최대 기력과 공통 대기시간은 0 이상이어야 합니다.");
            }
            foreach (string key in new[] { "BurningMaxStacks", "BarrierMaxStacks", "DizzinessThreshold", "DizzinessMaxStacks",
                "HealthBoostMaxStacks", "ExposedFlawMaxStacks", "BecomeFireMaxStacks", "SharpEdgeMaxStacks", "CombatMomentumMaxStacks" })
            {
                float value = Setting(key);
                if (value < 1f || value != (int)value)
                {
                    throw new FormatException("효과 중첩 설정은 양의 정수여야 합니다: " + key);
                }
            }
            foreach (string key in new[] { "BurningDamagePerStack", "DizzinessStunSeconds", "HealthBoostPerStack" })
            {
                if (Setting(key) < 0f)
                {
                    throw new FormatException("효과 수치/지속시간은 0 이상이어야 합니다: " + key);
                }
            }
            for (int index = 0; index < 6; index++)
            {
                Setting("MaxEnergy" + (MajorStatType)index);
            }
            foreach (var row in CsvTable.Read(texts, "SkillTextTable", "Key", "Value"))
            {
                if (string.IsNullOrWhiteSpace(row[1]) || !_texts.TryAdd(Required(row[0]), row[1]))
                {
                    throw new FormatException("스킬 문구가 비어 있거나 중복됩니다: " + row[0]);
                }
            }
        }

        public SkillDefinition GetSkill(string id)
        {
            return id != null && _skills.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException("스킬 정의 누락: " + id);
        }

        public SkillClassDefinition GetClass(string id)
        {
            return id != null && _classes.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException("직업 정의 누락: " + id);
        }

        public SkillWeaponDefinition GetWeapon(string id)
        {
            return id != null && _weapons.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException("무기 정의 누락: " + id);
        }

        public SkillClassDefinition GetClassForCharacter(string characterId)
        {
            return characterId != null && _characterClasses.TryGetValue(characterId, out string id) ? GetClass(id) : throw new KeyNotFoundException("인물의 직업 배정 누락: " + characterId);
        }

        public SkillClassDefinition GetClassForCharacter(CharacterProgress character)
        {
            if (character == null)
            {
                throw new ArgumentNullException(nameof(character));
            }
            return string.IsNullOrEmpty(character.WeaponId)
                ? GetClassForCharacter(character.Id) : GetClassForWeapon(character.WeaponId);
        }

        public SkillClassDefinition GetClassForWeapon(string weaponId)
        {
            foreach (var definition in Classes)
            {
                if (definition.WeaponId == weaponId)
                {
                    return definition;
                }
            }
            throw new KeyNotFoundException("무기에 대응하는 직업이 없습니다: " + weaponId);
        }

        public float Setting(string key)
        {
            return _settings.TryGetValue(key, out float value) ? value : throw new KeyNotFoundException("스킬 설정 누락: " + key);
        }

        public string Text(string key)
        {
            return _texts.TryGetValue(key, out string value) ? value : throw new KeyNotFoundException("스킬 문구 누락: " + key);
        }

        private void ValidatePrerequisites(SkillDefinition skill, HashSet<string> path)
        {
            if (!path.Add(skill.Id))
            {
                throw new FormatException("스킬 선행 조건 순환: " + skill.Id);
            }
            foreach (string id in skill.PrerequisiteIds)
            {
                var prerequisite = GetSkill(id);
                if (prerequisite.IsBasicAttack || prerequisite.TreeKind != skill.TreeKind || prerequisite.TreeId != skill.TreeId)
                {
                    throw new FormatException("스킬 선행 조건의 트리가 다릅니다: " + skill.Id);
                }
                ValidatePrerequisites(prerequisite, path);
            }
            path.Remove(skill.Id);
        }

        internal static string Required(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
            {
                throw new FormatException("스킬 시트의 필수 문자열을 확인하세요.");
            }
            return value;
        }

        internal static string[] List(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return Array.Empty<string>();
            }
            string[] values = value.Split(';');
            foreach (string item in values)
            {
                Required(item);
            }
            return values;
        }

        internal static int Integer(string value, int min, int max)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed < min || parsed > max)
            {
                throw new FormatException("스킬 정수 범위 오류: " + value);
            }
            return parsed;
        }

        internal static float Number(string value, float min, float max)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) || float.IsNaN(parsed) || float.IsInfinity(parsed) || parsed < min || parsed > max)
            {
                throw new FormatException("스킬 수치 범위 오류: " + value);
            }
            return parsed;
        }

        internal static T EnumValue<T>(string value) where T : struct, Enum
        {
            if (!Enum.TryParse(value, false, out T parsed) || !Enum.IsDefined(typeof(T), parsed) || value != parsed.ToString())
            {
                throw new FormatException("스킬 분류 오류: " + value);
            }
            return parsed;
        }
    }
}
