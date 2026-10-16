using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using MoreUIPanel;
using UnityEngine;
using UnityEngine.SceneManagement;
using static HarmonyLib.AccessTools;

namespace RunBasedPassives
{
	[BepInPlugin("cn_xc.RunBasedPassives", "RunBasedPassives", "0.4.1")]
	[BepInDependency("cn_xc.MoreUIPanel")]
	public class Plugin : BaseUnityPlugin
	{
		// 开关
		private ConfigEntry<bool> enableJump;
		private ConfigEntry<bool> enableStrength;
		private ConfigEntry<bool> enableStamina;
		private ConfigEntry<bool> enableHealth;
		private ConfigEntry<bool> enableSprintSpeed;
		private ConfigEntry<bool> enableCrouchRestore;
		private ConfigEntry<bool> verboseLogging;
		private ConfigEntry<int> sprintSpeedCap;

		// 等级
		public static int JumpLevel;
		public static int StrengthLevel;
		public static int StaminaLevel;
		public static int HealthLevel;
		public static int SprintSpeedLevel;
		public static int CrouchRestoreLevel;

		// 计数器
		public static int JumpCount;
		public static int GrabCount;
		public static float MoveDistance;
		public static int HealCount;
		public static float SprintDistance;
		public static float CrouchRestoreAmount;
		public static float LastEnergyCurrent = -1f;

		// 日志去重
		private static int _lastLogJump, _lastLogGrab, _lastLogHeal, _lastLogCrouch;
		private static float _lastLogMove, _lastLogSprint;

		// 动态阈值
		private int GetJumpThreshold() => 80 + JumpLevel * 40;
		private int GetGrabThreshold() => 50 + StrengthLevel * 10;
		private float GetMoveThreshold() => 500f + StaminaLevel * 200f;
		private int GetHealThreshold() => 10 + HealthLevel * 3;
		private float GetSprintThreshold() => 300f + SprintSpeedLevel * 100f;
		private float GetCrouchThreshold() => 80f + CrouchRestoreLevel * 20f;

		private Harmony harmony;

		// 反射
		private static FieldRef<PlayerController, int> jumpExtraRef;
		private static FieldRef<PlayerHealth, int> maxHealthRef;
		private static FieldRef<PlayerAvatar, string> steamIDRef;
		private static FieldRef<PlayerAvatar, bool> isLocalRef;
		private static FieldRef<PlayerHealth, PlayerAvatar> healthPlayerAvatarRef;
		private static FieldRef<StatsManager, SortedDictionary<string, Dictionary<string, int>>> dictOfDictsRef;
		private static FieldRef<PlayerAvatar, bool> crouchRestActiveRef;
		private static FieldRef<PlayerAvatar, float> crouchRestRef;
		private static FieldRef<StatsManager, string> saveFileCurrentRef;

		private bool _uiRegistered;
		private float _staminaBase = -1f;
		private float _originalSprintSpeed = -1f;
		private string _lastSaveFile = "";

		void Awake()
		{
			// 绑定配置
			enableJump = Config.Bind("Toggles", "EnableJump", true, "启用跳跃升级");
			enableStrength = Config.Bind("Toggles", "EnableStrength", true, "启用力量升级");
			enableStamina = Config.Bind("Toggles", "EnableStamina", true, "启用耐力升级");
			enableHealth = Config.Bind("Toggles", "EnableHealth", true, "启用生命升级");
			enableSprintSpeed = Config.Bind("Toggles", "EnableSprintSpeed", true, "启用冲刺速度升级");
			enableCrouchRestore = Config.Bind("Toggles", "EnableCrouchRestore", true, "启用蹲伏恢复升级");
			verboseLogging = Config.Bind("Debug", "VerboseLogging", false, "详细日志输出（用于调试）");
			sprintSpeedCap = Config.Bind("Thresholds", "SprintSpeedCap", 0,
				new ConfigDescription("冲刺速度封顶等级（0=不封顶）",
				new AcceptableValueRange<int>(0, int.MaxValue)));

			// 反射
			jumpExtraRef = AccessTools.FieldRefAccess<PlayerController, int>("JumpExtra");
			maxHealthRef = AccessTools.FieldRefAccess<PlayerHealth, int>("maxHealth");
			steamIDRef = AccessTools.FieldRefAccess<PlayerAvatar, string>("steamID");
			isLocalRef = AccessTools.FieldRefAccess<PlayerAvatar, bool>("isLocal");
			healthPlayerAvatarRef = AccessTools.FieldRefAccess<PlayerHealth, PlayerAvatar>("playerAvatar");
			dictOfDictsRef = AccessTools.FieldRefAccess<StatsManager, SortedDictionary<string, Dictionary<string, int>>>("dictionaryOfDictionaries");
			crouchRestActiveRef = AccessTools.FieldRefAccess<PlayerAvatar, bool>("upgradeCrouchRestActive");
			crouchRestRef = AccessTools.FieldRefAccess<PlayerAvatar, float>("upgradeCrouchRest");
			saveFileCurrentRef = AccessTools.FieldRefAccess<StatsManager, string>("saveFileCurrent");

			Patches.isLocalRef = isLocalRef;
			Patches.steamIDRef = steamIDRef;
			Patches.healthPlayerAvatarRef = healthPlayerAvatarRef;
			Patches.crouchRestActiveRef = crouchRestActiveRef;
			Patches.crouchRestRef = crouchRestRef;
			Patches.pluginLogger = Logger;
			Patches.verboseLogging = verboseLogging;

			Harmony.DEBUG = true;
			harmony = new Harmony("cn_xc.RunBasedPassives");

			harmony.Patch(AccessTools.Method(typeof(PlayerAvatar), "Jump"),
				prefix: new HarmonyMethod(typeof(Patches), nameof(Patches.JumpPrefix)));
			harmony.Patch(AccessTools.Method(typeof(PhysGrabObject), "GrabStarted"),
				postfix: new HarmonyMethod(typeof(Patches), nameof(Patches.GrabStartedPostfix)));
			harmony.Patch(AccessTools.Method(typeof(PlayerAvatar), "Update"),
				postfix: new HarmonyMethod(typeof(Patches), nameof(Patches.PlayerAvatarUpdatePostfix)));
			harmony.Patch(AccessTools.Method(typeof(PlayerHealth), "Heal"),
				prefix: new HarmonyMethod(typeof(Patches), nameof(Patches.HealPrefix)));

			SceneManager.sceneLoaded += OnSceneLoaded;

			Logger.LogInfo($"Harmony 补丁总数: {harmony.GetPatchedMethods().Count()}");
			Logger.LogInfo("RunBasedPassives v0.4.1 loaded");
			if (verboseLogging.Value)
				Logger.LogInfo("详细日志已开启（仅在经验变化时输出）");
		}

		void OnDestroy()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
			if (_uiRegistered) MoreUIPanel.Plugin.UnregisterLine("runbasedpassives_stats");
			harmony.UnpatchAll();
		}

		private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ResetCounters();

		private void ResetCounters()
		{
			// 获取当前存档标识
			string currentSave = saveFileCurrentRef.Invoke(StatsManager.instance) ?? "";

			if (_lastSaveFile != currentSave)
			{
				// 切换存档：完全归零
				_lastSaveFile = currentSave;
				JumpCount = 0;
				GrabCount = 0;
				MoveDistance = 0f;
				HealCount = 0;
				SprintDistance = 0f;
				CrouchRestoreAmount = 0f;

				if (verboseLogging.Value)
					Logger.LogInfo("[场景加载] 检测到存档切换，经验已清零");
			}
			else
			{
				// 同一存档内换层：保留余数
				JumpCount = JumpCount % GetJumpThreshold();
				GrabCount = GrabCount % GetGrabThreshold();
				MoveDistance = MoveDistance % GetMoveThreshold();
				HealCount = HealCount % GetHealThreshold();
				SprintDistance = SprintDistance % GetSprintThreshold();
				CrouchRestoreAmount = CrouchRestoreAmount % GetCrouchThreshold();

				if (verboseLogging.Value)
					Logger.LogInfo($"[场景加载] 经验已继承: 跳跃:{JumpCount}/{GetJumpThreshold()} 力量:{GrabCount}/{GetGrabThreshold()} 耐力:{MoveDistance:F0}/{GetMoveThreshold()} 生命:{HealCount}/{GetHealThreshold()} 冲刺:{SprintDistance:F0}/{GetSprintThreshold()} 蹲伏:{CrouchRestoreAmount:F0}/{GetCrouchThreshold()}");
			}

			_staminaBase = -1f;
			_originalSprintSpeed = -1f;
			LastEnergyCurrent = -1f;
			Patches.ResetState();
		}

		void Update()
		{
			if (PlayerAvatar.instance == null) return;
			bool inGameplay = SemiFunc.RunIsLevel() || SemiFunc.RunIsShop();

			if (inGameplay && !_uiRegistered)
			{
				MoreUIPanel.Plugin.RegisterLine("runbasedpassives_stats", BuildDisplayText);
				_uiRegistered = true;
			}
			else if (!inGameplay && _uiRegistered)
			{
				MoreUIPanel.Plugin.UnregisterLine("runbasedpassives_stats");
				_uiRegistered = false;
			}
			if (!inGameplay) return;

			string sid = GetSteamID();
			if (!string.IsNullOrEmpty(sid))
			{
				JumpLevel = ReadGameDict("playerUpgradeExtraJump", sid);
				StrengthLevel = ReadGameDict("playerUpgradeStrength", sid);
				StaminaLevel = ReadGameDict("playerUpgradeStamina", sid);
				HealthLevel = ReadGameDict("playerUpgradeHealth", sid);
				SprintSpeedLevel = ReadGameDict("playerUpgradeSpeed", sid);
				CrouchRestoreLevel = ReadGameDict("playerUpgradeCrouchRest", sid);
			}

			if (_staminaBase <= 0f && PlayerController.instance != null)
				_staminaBase = PlayerController.instance.EnergyStart;

			CheckUpgrades();
			ApplyStats();

			if (verboseLogging.Value)
			{
				bool crouchChanged = (int)CrouchRestoreAmount != _lastLogCrouch;
				if (JumpCount != _lastLogJump || GrabCount != _lastLogGrab ||
					(int)MoveDistance != (int)_lastLogMove || HealCount != _lastLogHeal ||
					(int)SprintDistance != (int)_lastLogSprint || crouchChanged)
				{
					Logger.LogInfo($"[状态] 跳跃:{JumpCount}/{GetJumpThreshold()} Lv.{JumpLevel} | 力量:{GrabCount}/{GetGrabThreshold()} Lv.{StrengthLevel} | 耐力:{MoveDistance:F0}/{GetMoveThreshold()} Lv.{StaminaLevel} | 生命:{HealCount}/{GetHealThreshold()} Lv.{HealthLevel} | 冲刺:{SprintDistance:F0}/{GetSprintThreshold()} Lv.{SprintSpeedLevel} | 蹲伏:{CrouchRestoreAmount:F0}/{GetCrouchThreshold()} Lv.{CrouchRestoreLevel}");
					_lastLogJump = JumpCount; _lastLogGrab = GrabCount;
					_lastLogMove = MoveDistance; _lastLogHeal = HealCount;
					_lastLogSprint = SprintDistance; _lastLogCrouch = (int)CrouchRestoreAmount;
				}
			}
		}

		private int ReadGameDict(string dictName, string sid)
		{
			var dicts = dictOfDictsRef.Invoke(StatsManager.instance);
			if (dicts != null && dicts.TryGetValue(dictName, out var inner) && inner != null)
				if (inner.TryGetValue(sid, out int val)) return val;
			return 0;
		}

		void CheckUpgrades()
		{
			string sid = GetSteamID();
			if (string.IsNullOrEmpty(sid)) return;
			bool upgraded = false;

			if (enableJump.Value && JumpCount >= GetJumpThreshold())
			{
				JumpCount -= GetJumpThreshold();
				JumpLevel++;
				StatsManager.instance?.DictionaryUpdateValue("playerUpgradeExtraJump", sid, JumpLevel);
				if (verboseLogging.Value) Logger.LogInfo($"[升级] 跳跃升级至 Lv.{JumpLevel}");
				upgraded = true;
			}
			if (enableStrength.Value && GrabCount >= GetGrabThreshold())
			{
				GrabCount -= GetGrabThreshold();
				StrengthLevel++;
				StatsManager.instance?.DictionaryUpdateValue("playerUpgradeStrength", sid, StrengthLevel);
				if (verboseLogging.Value) Logger.LogInfo($"[升级] 力量升级至 Lv.{StrengthLevel}");
				upgraded = true;
			}
			if (enableStamina.Value && MoveDistance >= GetMoveThreshold())
			{
				MoveDistance -= GetMoveThreshold();
				StaminaLevel++;
				StatsManager.instance?.DictionaryUpdateValue("playerUpgradeStamina", sid, StaminaLevel);
				if (verboseLogging.Value) Logger.LogInfo($"[升级] 耐力升级至 Lv.{StaminaLevel}");
				upgraded = true;
			}
			if (enableHealth.Value && HealCount >= GetHealThreshold())
			{
				HealCount -= GetHealThreshold();
				HealthLevel++;
				StatsManager.instance?.DictionaryUpdateValue("playerUpgradeHealth", sid, HealthLevel);
				if (verboseLogging.Value) Logger.LogInfo($"[升级] 生命升级至 Lv.{HealthLevel}");
				upgraded = true;
			}
			if (enableSprintSpeed.Value && SprintDistance >= GetSprintThreshold())
			{
				SprintDistance -= GetSprintThreshold();
				SprintSpeedLevel++;
				StatsManager.instance?.DictionaryUpdateValue("playerUpgradeSpeed", sid, SprintSpeedLevel);
				if (verboseLogging.Value) Logger.LogInfo($"[升级] 冲刺速度升级至 Lv.{SprintSpeedLevel}");
				upgraded = true;
			}
			if (enableCrouchRestore.Value && CrouchRestoreAmount >= GetCrouchThreshold())
			{
				CrouchRestoreAmount -= GetCrouchThreshold();
				CrouchRestoreLevel++;
				StatsManager.instance?.DictionaryUpdateValue("playerUpgradeCrouchRest", sid, CrouchRestoreLevel);
				if (verboseLogging.Value) Logger.LogInfo($"[升级] 蹲伏恢复升级至 Lv.{CrouchRestoreLevel}");
				upgraded = true;
			}

			if (upgraded && StatsUI.instance != null)
				StatsUI.instance.Fetch();
		}

		void ApplyStats()
		{
			PlayerController pc = PlayerController.instance;
			PlayerAvatar av = PlayerAvatar.instance;
			if (pc == null || av == null) return;

			if (enableJump.Value)
				jumpExtraRef.Invoke(pc) = JumpLevel;

			if (enableStrength.Value)
			{
				PhysGrabber grabber = av.physGrabber;
				if (grabber != null) grabber.grabStrength = 1f + StrengthLevel;
			}

			if (enableStamina.Value)
			{
				float baseStamina = (_staminaBase > 0f) ? _staminaBase : 100f;
				pc.EnergyStart = baseStamina + StaminaLevel * 10;
				if (pc.EnergyCurrent > pc.EnergyStart)
					pc.EnergyCurrent = pc.EnergyStart;
			}

			if (enableHealth.Value)
			{
				PlayerHealth ph = av.playerHealth;
				if (ph != null) maxHealthRef.Invoke(ph) = 100 + HealthLevel * 20;
			}

			if (enableSprintSpeed.Value)
			{
				if (_originalSprintSpeed < 0f)
					_originalSprintSpeed = pc.SprintSpeed;

				int effectiveLevel = SprintSpeedLevel;
				if (sprintSpeedCap.Value > 0 && effectiveLevel > sprintSpeedCap.Value)
					effectiveLevel = sprintSpeedCap.Value;

				pc.SprintSpeed = _originalSprintSpeed + effectiveLevel * 1.0f;
			}
		}

		private string GetSteamID()
		{
			PlayerAvatar av = PlayerAvatar.instance;
			if (av == null) return null;
			return steamIDRef.Invoke(av);
		}

		private string BuildDisplayText()
		{
			StringBuilder sb = new StringBuilder();
			if (enableJump.Value) sb.AppendLine($"跳跃 Lv.{JumpLevel} ({JumpCount}/{GetJumpThreshold()})");
			if (enableStrength.Value) sb.AppendLine($"力量 Lv.{StrengthLevel} ({GrabCount}/{GetGrabThreshold()})");
			if (enableStamina.Value) sb.AppendLine($"耐力 Lv.{StaminaLevel} ({MoveDistance:F0}/{GetMoveThreshold()})");
			if (enableHealth.Value) sb.AppendLine($"生命 Lv.{HealthLevel} ({HealCount}/{GetHealThreshold()})");
			if (enableSprintSpeed.Value) sb.AppendLine($"冲刺 Lv.{SprintSpeedLevel} ({SprintDistance:F0}/{GetSprintThreshold()})");
			if (enableCrouchRestore.Value) sb.AppendLine($"蹲伏 Lv.{CrouchRestoreLevel} ({CrouchRestoreAmount:F0}/{GetCrouchThreshold()})");
			return sb.ToString().TrimEnd();
		}
	}

	public static class Patches
	{
		internal static FieldRef<PlayerAvatar, bool> isLocalRef;
		internal static FieldRef<PlayerAvatar, string> steamIDRef;
		internal static FieldRef<PlayerHealth, PlayerAvatar> healthPlayerAvatarRef;
		internal static FieldRef<PlayerAvatar, bool> crouchRestActiveRef;
		internal static FieldRef<PlayerAvatar, float> crouchRestRef;
		internal static BepInEx.Logging.ManualLogSource pluginLogger;
		internal static ConfigEntry<bool> verboseLogging;

		private static Vector3 lastPos, lastSprintPos;
		private static bool lastPosInit, lastSprintPosInit;
		private static readonly HashSet<int> grabbedThisLevel = new HashSet<int>();

		private static bool IsInGameplay => SemiFunc.RunIsLevel() || SemiFunc.RunIsShop();

		public static void ResetState()
		{
			lastPosInit = lastSprintPosInit = false;
			grabbedThisLevel.Clear();
		}

		public static void JumpPrefix(PlayerAvatar __instance)
		{
			if (!IsInGameplay || !isLocalRef.Invoke(__instance)) return;
			Plugin.JumpCount++;
		}

		public static void GrabStartedPostfix(PhysGrabObject __instance)
		{
			if (!IsInGameplay || SemiFunc.RunIsShop()) return;
			int id = __instance.GetInstanceID();
			if (grabbedThisLevel.Contains(id)) return;

			var grabbers = __instance.playerGrabbing;
			if (grabbers == null) return;
			foreach (var g in grabbers)
			{
				if (g.playerAvatar != null && isLocalRef.Invoke(g.playerAvatar))
				{
					grabbedThisLevel.Add(id);
					Plugin.GrabCount++;
					break;
				}
			}
		}

		public static void PlayerAvatarUpdatePostfix(PlayerAvatar __instance)
		{
			if (!IsInGameplay || !isLocalRef.Invoke(__instance)) return;

			Vector3 pos = __instance.transform.position;

			// 移动距离（耐力）
			if (!lastPosInit) { lastPos = pos; lastPosInit = true; return; }
			float dist = Vector3.Distance(pos, lastPos);
			Plugin.MoveDistance += dist;
			lastPos = pos;

			// 冲刺距离
			if (PlayerController.instance != null && PlayerController.instance.sprinting)
			{
				if (!lastSprintPosInit) { lastSprintPos = pos; lastSprintPosInit = true; return; }
				float sprintDist = Vector3.Distance(pos, lastSprintPos);
				Plugin.SprintDistance += sprintDist;
			}
			lastSprintPos = pos;
			lastSprintPosInit = true;

			// 蹲伏恢复：基于实际体力增量累积经验
			if (crouchRestActiveRef.Invoke(__instance))
			{
				PlayerController pc = PlayerController.instance;
				if (pc != null)
				{
					float current = pc.EnergyCurrent;
					if (Plugin.LastEnergyCurrent >= 0f)
					{
						float delta = current - Plugin.LastEnergyCurrent;
						if (delta > 0f)
							Plugin.CrouchRestoreAmount += delta;
					}
					Plugin.LastEnergyCurrent = current;
				}
			}
			else
			{
				Plugin.LastEnergyCurrent = -1f;
			}
		}

		public static void HealPrefix(int healAmount, PlayerHealth __instance)
		{
			if (!IsInGameplay || SemiFunc.RunIsShop()) return;
			if (healAmount <= 0) return;
			PlayerAvatar av = healthPlayerAvatarRef.Invoke(__instance);
			if (av == null || !isLocalRef.Invoke(av)) return;
			Plugin.HealCount++;
		}
	}
}