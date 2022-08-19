//#define DEBUGMODE
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Modding;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.UI;
using GlobalEnums;

namespace Exaltation
{
#if DEBUGMODE
#warning ####################################
#warning DEBUG MODE ACTIVE - DO NOT SHIP THIS
#warning ####################################
#endif
	public class Exaltation : Mod, IMenuMod, ITogglableMod, ILocalSettings<SaveSettings>
	{

		private const float BASE_SPEED = 8.3f;
		private const float BASE_SPEED_CH = 10f; //sprintmaster - 20% increase
		private const float BASE_SPEED_CH_COMBO = 11.5f; //sprintmaster + dashmaster = 37% increase
		private const float BASE_SPEED_CH_GLORY = 11.62f; //glorified sprintmaster = ~40% increase
		private const float BASE_SPEED_CH_GLORY_COMBO = 12.1f; //glorified sprintmaster + dashmaster = 45% increase
		private const float BASE_SPEED_CH_GLORYMACHINEWOKE = 12.45f; //glorified sprintmaster + glorified dashmaster = 50% increase

		private const float BASE_FOCUS_SPEED_CH = 0.018f;
		private const float SWIFT_FOCUS_SPEED_CH = 0.01f;

		private const float BASE_ATTACK_DURATION_CH = 0.28f;
		private const float BASE_ATTACK_COOLDOWN_CH = 0.25f;
		private const float STEEL_TEMPEST_ATTACK_DURATION = 0.1f;
		private const float STEEL_TEMPEST_ATTACK_COOLDOWN = 0.05f;

		private const float BASE_HIVEBLOOD_SPEED = 5f; //10 seconds
		private const float AMPOULE_HIVEBLOOD_SPEED = 4f; //8 seconds

		private const int BASE_HATCHLING_COST = 8;

		private float StoneshellRegenTime = 0f;
		private const float STONESHELL_REGEN_WAIT = 10f;

		private const int MONOMON_LENS_SOUL_PER_DAMAGE = 4;
		private const float MONOMON_LENS_MAX_INCREASE = 25;

		private float NailsageRegenTime = 0f;
		private const float NAILSAGE_SOUL_WAIT = 0.75f;
		private const int NAILSAGE_SOUL_REGEN = 4;

		private const int KINGSMOULD_CARAPACE_BASE_SOUL = 33;
		private const float KINGSMOULD_REGEN_WAIT = 0.33f;
		private int KingsmouldCarapaceSoulCost = KINGSMOULD_CARAPACE_BASE_SOUL;
		private float KingsmouldCarapaceTimer = 0f;

		private const int DREAM_CATCHER_COST = 10;

		private bool WyrmfuryDeathProtection = true;
		private GameObject CanvasObject;
		private GameObject TextCanvas; //use a different canvas for text since it's handled differently
		private Text TextObject;
		private GameObject WyrmfuryIcon;

		private bool DebugNoGlorification = false; //Set this to true to disable all glorifications
		private bool DebugAllGlories = false; //Set this to true to make all charms eligible for glorification

		internal static Exaltation Instance;
		public override string GetVersion() => Assembly.GetExecutingAssembly().GetName().Version.ToString();
		internal Dictionary<string, Sprite> Sprites;
		internal Dictionary<string, Sprite> CachedSprites;
		internal Coroutine AlterSprites;
		private static FieldInfo GeoControlSize = typeof(GeoControl).GetField("size", BindingFlags.NonPublic | BindingFlags.Instance);
		private static MethodInfo ClinkClink = typeof(GeoControl).GetMethod("PlayCollectSound", BindingFlags.NonPublic | BindingFlags.Instance);
		private static readonly FieldInfo SpriteField = typeof(HeroController).GetField("spriteFlash", BindingFlags.Instance | BindingFlags.NonPublic);
		private static FieldInfo ShadowDashCD = typeof(HeroController).GetField("shadowDashTimer", BindingFlags.NonPublic | BindingFlags.Instance);

		private int[] CharmNums = new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 14, 16, 19, 20, 21, 22, 26, 27, 29, 30, 31, 32, 33, 35, 37 }; //the charm numbers that can be glorified go here for sprites and the like

		public SaveSettings Settings = new SaveSettings();
		public void OnLoadLocal(SaveSettings s) => Settings = s;
		public SaveSettings OnSaveLocal() => Settings;

		// Mod menu
		public bool ToggleButtonInsideMenu => true;
		public List<IMenuMod.MenuEntry> GetMenuData(IMenuMod.MenuEntry? toggleButtonEntry)
		{
			return new List<IMenuMod.MenuEntry> { toggleButtonEntry.Value };
		}

		public void OnHeroUpdate()
		{
			MakeCanvas();
            if (AlterSprites != null)
                GameManager.instance.StopCoroutine(AlterSprites);
            AlterSprites = GameManager.instance.StartCoroutine(ChangeSprites());
            float timer = Time.deltaTime;
			if (WearingGlorifiedCharm("FuryOfTheFallen"))
				UpdateWyrmfuryIcon();
			if (WearingGlorifiedCharm("BaldurShell") && PlayerData.instance.blockerHits < 4)
			{
				StoneshellRegenTime -= timer;
				if (StoneshellRegenTime <= 0)
				{
					StoneshellRegenTime = STONESHELL_REGEN_WAIT;
					PlayerData.instance.blockerHits++;
					ReflectionHelper.GetField<HeroController, AudioSource>(HeroController.instance, "audioSource")
						.PlayOneShot(LoadAssets.BlockerSound, 1f);
					((SpriteFlash)SpriteField.GetValue(HeroController.instance)).flash(Color.blue, 0.5f, 0.0f, 0.0f, 0.5f);
				}
			}
			if (WearingGlorifiedCharm("StalwartShell") && KingsmouldCarapaceSoulCost > KINGSMOULD_CARAPACE_BASE_SOUL)
			{
				KingsmouldCarapaceTimer -= timer;
				if (KingsmouldCarapaceTimer <= 0)
				{
					KingsmouldCarapaceSoulCost--;
					KingsmouldCarapaceTimer = KINGSMOULD_REGEN_WAIT;
				}
			}
			if (HeroController.instance.cState.nearBench && (WearingGlorifiedCharm("SoulCatcher") || WearingGlorifiedCharm("SoulEater")))
				HeroController.instance.AddMPChargeSpa(1);
			if (WearingGlorifiedCharm("NailmastersGlory") && Settings.NMGPatience)
			{
				NailsageRegenTime -= Time.deltaTime;
				if (NailsageRegenTime <= 0)
				{
					NailsageRegenTime = NAILSAGE_SOUL_WAIT;
					HeroController.instance.AddMPCharge(GainSoul(NAILSAGE_SOUL_REGEN));
				}
			}
		}

		public string LanguageGet(string key, string sheet, string orig)
		{
			if (IsGlorified("GatheringSwarm"))
			{
				if (key == "CHARM_NAME_1")
					return "Symbol of Avarice";
				else if (key == "CHARM_DESC_1")
					return "Prized possession of a powerful bug who fell to their own greed.\n\n" +
						"Geo will be transferred to your hoard instead of dropping onto the ground, ensuring that every last piece is collected and put into its rightful place.";
			}
			if (IsGlorified("WaywardCompass"))
			{
				if (key == "CHARM_NAME_2")
					return "Lifeseed Lantern";
				else if (key == "CHARM_DESC_2")
					return "Glass lantern containing a Lifeseed. It is said that Lifeseeds' antennae will always face northward.\n\n" +
						"The bearer will be able to pinpoint their current location on their map, and gain a very modest lifeblood coating.\n\n" +
						"Requires no charm notches.";
			}
			if (IsGlorified("Grubsong"))
			{
				if (key == "CHARM_NAME_3")
					return "Grubberfly Hymn";
				else if (key == "CHARM_DESC_3")
					return "Contains the tenacity of the grubberfly.\n\n" +
						"Gain SOUL when taking damage, and gain more SOUL when striking foes with the nail based upon missing health.";
			}
			if (IsGlorified("StalwartShell"))
			{
				if (key == "CHARM_NAME_4")
					return "Kingsmould Carapace";
				else if (key == "CHARM_DESC_4")
					return "White metal vessel used to shape and harness void material.\n\n" +
						"The bearer will remain invulnerable for longer when recovering from damage. Additionally, their SOUL will be used, in increasing amounts, to lower the damage of overwhelming strikes against them.";
			}
			if (IsGlorified("BaldurShell"))
			{
				if (key == "CHARM_NAME_5")
					return "Baldur Stoneshell";
				else if (key == "CHARM_DESC_5")
					return "Rocky exoskeleton that protects its bearer with a hard shell while focusing SOUL.\n\n" +
						"The shell is not indestructible, but slowly repairs damage over time.";
			}
			if (IsGlorified("FuryOfTheFallen"))
			{
				if (key == "CHARM_NAME_6")
					return (Settings.FotFShade ? "Shade" : "Wyrm") + "fury";
				else if (key == "CHARM_DESC_6")
					return Settings.FotFShade ?
						"Charm embodying the void's patience and resilience.\n\n" +
						"When close to death, the energy contained within will fill the bearer with stillness and cold focus, and will absorb a single blow that would strike them down." :
						"Charm born of Hallownest's refusal to bend to the old light.\n\n" +
						"When close to death, the energy contained within will fill its bearer with the courage to defy death, and will absorb a single blow that would strike them down.";
			}
			if (IsGlorified("QuickFocus"))
			{
				if (key == "CHARM_NAME_7")
					return "Swift Focus";
				else if (key == "CHARM_DESC_7")
					return "A charm containing crystallized SOUL.\n\n" +
						"Greatly increases the speed of focusing SOUL.";
			}
			if (IsGlorified("LifebloodHeart"))
			{
				if (key == "CHARM_NAME_8")
					return "Lifeblood Crux";
				else if (key == "CHARM_DESC_8")
					return "Contains a living core that exudes precious lifeblood.\n\n" +
						"When resting, the bearer will gain a coating of lifeblood that protects from a large amount of damage.";
			}
			if (IsGlorified("LifebloodCore"))
			{
				if (key == "CHARM_NAME_9")
					return "Lifeblood Nucleus";
				else if (key == "CHARM_DESC_9")
					return "Contains a living core that flows with precious lifeblood.\n\n" +
						"When resting, the bearer will gain a coating of lifeblood that protects from a great amount of damage.";
			}
			if (IsGlorified("ThornsOfAgony"))
			{
				if (key == "CHARM_NAME_12")
					return "Palace Rose";
				else if (key == "CHARM_DESC_12")
					return "Hardy, colorless rose grown in the White Palace. Bristles with menacing thorns.\n\n" +
						"When taking damage, sprout mystical vines that greatly damage nearby foes.";
			}
			if (IsGlorified("SteadyBody"))
			{
				if (key == "CHARM_NAME_14")
					return "Garden Sprig";
				else if (key == "CHARM_DESC_14")
					return "Strip of plant material taken from the Queen's Gardens.\n\n" +
						"Keeps its bearer from recoiling backwards when they strike an enemy with a nail.\n\n" +
						"Requires no charm notches.";
			}
			if (IsGlorified("SharpShadow"))
			{
				if (key == "CHARM_NAME_16")
					return "Razor Shadow";
				else if (key == "CHARM_DESC_16")
					return "Contains a whispering, eldritch spell that sharpens shadows into vicious weapons.\n\n" +
						"When using Shadow Dash, the bearer's body will cut through enemies like silk, and remain incorporeal for a short time afterwards.";
			}
			if (IsGlorified("ShamanStone"))
			{
				if (key == "CHARM_NAME_19")
					return "Shaman Relic";
				else if (key == "CHARM_DESC_19")
					return "Carved relic from within the Ancestral Mound. Said to contain the minds of past generations of shamans.\n\n" +
						"Greatly increases the power of spells, dealing much more damage to foes.";
			}
			if (IsGlorified("SoulCatcher"))
			{
				if (key == "CHARM_NAME_20")
					return "Soul Snare";
				else if (key == "CHARM_DESC_20")
					return "Used to capture large amounts of SOUL from the world around it.\n\n" +
						"Modestly increases the amount of SOUL gained when striking an enemy with the nail, and quickly regenerates SOUL while at a bench.";
			}
			if (IsGlorified("SoulEater"))
			{
				if (key == "CHARM_NAME_21")
					return "Soul Feeder";
				else if (key == "CHARM_DESC_21")
					return "Void liquid contained in a metal vessel, perfectly still. Endlessly consumes SOUL from the world around it.\n\n" +
						"Incomparably increases the amount of SOUL gained when striking an enemy with the nail, and quickly regenerates SOUL while at a bench.";
			}
			if (IsGlorified("GlowingWomb"))
			{
				if (key == "CHARM_NAME_22")
					return "Primal Womb";
				else if (key == "CHARM_DESC_22")
					return "Contains the innate will to propogate one's kind.\n\n" +
						"Rapidly drains the bearer's SOUL to create a swarm of hatchlings that will defend their parent.";
			}
			if (IsGlorified("NailmastersGlory"))
			{
				if (key == "CHARM_NAME_26")
					return Settings.NMGPatience ? "Sagesoul" : "Nailsage's Tenacity";
				else if (key == "CHARM_DESC_26")
					return Settings.NMGPatience ?
						"Charm ensorcelled with a fraction of the Kingsoul's power. Defeat a nailsage in the Hall of Gods to reverse the enchantment.\n\n" +
						"Improves the bearer's mastery of Nail Arts and slowly draws SOUL from the surrounding world.\n\n" +
						"The bearer's nail attacks will gain no SOUL, but will slice easily through armor." :
						"Contains the timeless ferocity and vigor of a Nailsage. Defeat a nailsage in the Hall of Gods to enchant it.\n\n" +
						"Improves the bearer's mastery of Nail Arts and increases the power of their nail strikes as they near death.";
			}
			if (IsGlorified("JonisBlessing"))
			{
				if (key == "CHARM_NAME_27")
					return "Joni's Benediction";
				else if (key == "CHARM_DESC_27")
					return "Revered by Joni, the kindly heretic. Transmogrifies vital fluids into blue lifeblood.\n\n" +
						"The bearer will have a healthier shell and can take much more damage, but they will not be able to heal themselves by focusing SOUL.";
			}
			if (IsGlorified("Hiveblood"))
			{
				if (key == "CHARM_NAME_29")
					return "Ambrosial Ampoule";
				else if (key == "CHARM_DESC_29")
					return "Golden nugget of hardened nectar from the Hive that has been compressed into a metal shell from the Crystal Peak.\n\n" +
						"Quickly heals the bearer's recent wounds over time, allowing them to regain some health without focusing SOUL.";
			}
			if (IsGlorified("Dashmaster"))
			{
				if (key == "CHARM_NAME_31")
					return "Marathon Master";
				else if (key == "CHARM_DESC_31")
					return "Bears the likeness of an eccentric bug known only as ‘The Dashmaster', in true form.\n\n" +
						"The bearer will be able to dash more often as well as dash downwards.";
			}
			if (IsGlorified("QuickSlash"))
			{
				if (key == "CHARM_NAME_32")
					return "Steel Tempest";
				else if (key == "CHARM_DESC_32")
					return "Bladed disc forged from pale ore. Emits a whistling sound when moving through the air.\n\n" +
						"The bearer's nail will become like a storm of metal, moderately decreasing its damage but tremendously increasing its swinging speed.";
			}
			if (IsGlorified("SpellTwister"))
			{
				if (key == "CHARM_NAME_33")
					return "Prismatic Lens";
				else if (key == "CHARM_DESC_33")
					return "Lens molded from fog that shifts and glimmers in the light.\n\n" +
						"Reduces the SOUL cost of casting spells, and empowers them based on remaining SOUL after the spell is cast.";
			}
			if (IsGlorified("Sprintmaster"))
			{
				if (key == "CHARM_NAME_37")
					return "Stagway Coin";
				else if (key == "CHARM_DESC_37")
					return "A tarnished symbol once held by the upper caste of Hallownest. Each coin allowed priority access to the Stagways, should its holder prefer the old paths.\n\n" +
						"Greatly increases the running speed of its bearer, allowing them to expedite their travels.";
			}
			return Language.Language.GetInternal(key, sheet);
		}

		private void BeforeSaveGameSave(SaveGameData data = null)
		{
			PlayerData.instance.charmCost_2 = 1;
			PlayerData.instance.charmCost_14 = 1;
			PlayerData.instance.charmCost_29 = 4;
			PlayerData.instance.charmCost_31 = 2;
		}

		private void SaveGameSave(int id = 0)
		{
			if (IsGlorified("WaywardCompass"))
				PlayerData.instance.charmCost_2 = 0;
			if (IsGlorified("SteadyBody"))
				PlayerData.instance.charmCost_14 = 0;
			if (IsGlorified("Hiveblood"))
				PlayerData.instance.charmCost_29 = 3;
			if (IsGlorified("Dashmaster"))
				PlayerData.instance.charmCost_31 = 1;
			Settings.LastVersion = GetVersion();
		}

		private void AfterSaveGameLoad(SaveGameData data)
		{
            if (AlterSprites != null)
                GameManager.instance.StopCoroutine(AlterSprites);
            AlterSprites = GameManager.instance.StartCoroutine(ChangeSprites());
        }

		private void ProcessGeoUpdate(On.GeoControl.orig_OnEnable orig, GeoControl self)
		{
			orig(self);
			if (WearingGlorifiedCharm("GatheringSwarm")) //with symbol of avarice, instantly transfer geo rather than drop it
			{
				GeoControl.Size size = (GeoControl.Size)GeoControlSize.GetValue(self);
				HeroController.instance.AddGeo(size.value); //get a reflection of however much geo there is total, so that we don't lose any
				ClinkClink.Invoke(self, null);
				self.Disable(0.05f);
			}
		}

		private void OnCharmUpdate(PlayerData pd, HeroController hc)
		{
			if (TextObject == null)
				MakeCanvas();
			else
			{
				if (hc != null)
				{
					bool glorified_this_update = false;
					foreach (int i in CharmNums)
					{
						string s = i.ToString();
						if (CanGlorify(s) && !IsGlorified(s))
						{
							GlorifyCharm(s);
							glorified_this_update = true;
						}
					}
					if (glorified_this_update)
						HeroController.instance.StartCoroutine(GloryEffects("Charms glorified through recent victories"));
				}
				if (PlayerData.instance.statueStateSly.completedTier1 && WearingGlorifiedCharm("NailmastersGlory"))
				{
					PlayerData.instance.statueStateSly.completedTier1 = false;
					if (!Settings.NMGPatience)
					{
						Settings.NMGPatience = true;
						hc.StartCoroutine(GloryEffects("Nailsage's Tenacy ensorcelled with the power of the Kingsoul"));
					}
					else
					{
						Settings.NMGPatience = false;
						hc.StartCoroutine(GloryEffects("Sagesoul imbued with the tenacity of a nailsage"));
					}
				}
			}
			UpdateMoveSpeed();
			AdjustOldValues();
			WyrmfuryDeathProtection = true; //reset death protection when resting
			if (WearingGlorifiedCharm("QuickSlash"))
			{
				hc.ATTACK_COOLDOWN_TIME_CH = STEEL_TEMPEST_ATTACK_COOLDOWN; //nyoooommmm
				hc.ATTACK_DURATION_CH = STEEL_TEMPEST_ATTACK_DURATION;
			}
			else
			{
				hc.ATTACK_COOLDOWN_TIME_CH = BASE_ATTACK_COOLDOWN_CH;
				hc.ATTACK_DURATION_CH = BASE_ATTACK_DURATION_CH;
			}
			GameObject helf = GameObject.Find("Health");
			if (helf != null)
			{
				helf.LocateMyFSM("Hive Health Regen").
					Fsm.GetFsmFloat("Recover Time").
					Value = WearingGlorifiedCharm("Hiveblood") ? AMPOULE_HIVEBLOOD_SPEED : BASE_HIVEBLOOD_SPEED;
			}

			GameObject churm = GameObject.Find("Charm Effects");
			if (churm != null)
			{
				churm.LocateMyFSM("Hatchling Spawn").
					Fsm.GetFsmInt("Hatchling Max").
					Value = WearingGlorifiedCharm("GlowingWomb") ? 8 : 4;
				churm.LocateMyFSM("Hatchling Spawn").
					Fsm.GetFsmFloat("Hatch Time").
					Value = WearingGlorifiedCharm("GlowingWomb") ? 2f : 4f;
				churm.LocateMyFSM("Hatchling Spawn").
					Fsm.GetFsmInt("Soul Cost").
					Value = WearingGlorifiedCharm("GlowingWomb") ? 4 : 8;
			}
			hc.spellControl.Fsm.GetFsmFloat("Time Per MP Drain CH").Value = WearingGlorifiedCharm("QuickFocus") ? SWIFT_FOCUS_SPEED_CH : BASE_FOCUS_SPEED_CH;
			pd.charmCost_2 = IsGlorified("WaywardCompass") ? 0 : 1;
			pd.charmCost_14 = IsGlorified("SteadyBody") ? 0 : 1;
			pd.charmCost_29 = IsGlorified("Hiveblood") ? 3 : 4;
			pd.charmCost_31 = IsGlorified("Dashmaster") ? 1 : 2;
		}

		private int TakeDamage(int amount)
		{
			PlayerData pd = PlayerData.instance;
			if (pd.maxHealth <= amount) //only protect from damage if we aren't at max health; mainly for radiant bosses
				return amount;
			else if (amount >= 2 && pd.MPCharge >= KingsmouldCarapaceSoulCost && WearingGlorifiedCharm("StalwartShell"))
			{
				amount--; // reduces high damage by 1 mask!
				HeroController.instance.TakeMP(KingsmouldCarapaceSoulCost);
				ReflectionHelper.GetField<HeroController, AudioSource>(HeroController.instance, "audioSource")
					.PlayOneShot(LoadAssets.ShellSound, 1f);
				KingsmouldCarapaceSoulCost *= 2;
				if (KingsmouldCarapaceSoulCost > 100)
					KingsmouldCarapaceSoulCost = 100;
			}
			if (pd.health <= amount && amount > 0 && WyrmfuryDeathProtection && WearingGlorifiedCharm("FuryOfTheFallen"))
			{
				amount = pd.health - 1; //brings to 1 HP if you're not there already
				WyrmfuryDeathProtection = false; //nullify the hit!
				ReflectionHelper.GetField<HeroController, AudioSource>(HeroController.instance, "audioSource")
					.PlayOneShot(LoadAssets.WyrmfurySound, 1f);
				GameCameras.instance.cameraShakeFSM.SendEvent("BigShake");
			}
			StoneshellRegenTime = STONESHELL_REGEN_WAIT; //prevent the hit from regenerating
			return amount;
		}

		private int LifebloodMasksRestored()
		{
			int masks = 0;
			if (WearingGlorifiedCharm("WaywardCompass"))
				masks++; //lifeseed!
			if (WearingGlorifiedCharm("LifebloodHeart"))
				masks += 2;
			if (WearingGlorifiedCharm("LifebloodCore"))
				masks += 2;
			if (WearingGlorifiedCharm("JonisBlessing"))
				masks += 4; //BIG MASKS
			return masks;
		}

		private void UpdateMoveSpeed(float mod = 1f)
		{
			if (!WearingGlorifiedCharm("Sprintmaster"))
			{
				HeroController.instance.RUN_SPEED = BASE_SPEED * mod;
				HeroController.instance.RUN_SPEED_CH = BASE_SPEED_CH * mod;
				HeroController.instance.RUN_SPEED_CH_COMBO = BASE_SPEED_CH_COMBO * mod;
			}
			else
			{
				HeroController.instance.RUN_SPEED_CH = BASE_SPEED_CH_GLORY * mod;
				HeroController.instance.RUN_SPEED_CH_COMBO = BASE_SPEED_CH_GLORY_COMBO * mod;
				if (WearingGlorifiedCharm("Dashmaster"))
					HeroController.instance.RUN_SPEED_CH_COMBO = BASE_SPEED_CH_GLORYMACHINEWOKE * mod;
			}
		}

		private int GainSoul(int amount)
		{
			if (WearingGlorifiedCharm("Grubsong"))
			{
				int MissingHealth = PlayerData.instance.maxHealth - PlayerData.instance.health;
				amount += (int)(0.5 * MissingHealth);
			}
			if (WearingGlorifiedCharm("SoulCatcher"))
				amount += 2; //Vanilla soul catcher is +3, so +2 = +5%
			if (WearingGlorifiedCharm("SoulEater"))
				amount += 3; //Vanilla soul eater is +8, so +3 = +11% - double the base!
			if (WearingGlorifiedCharm("NailmastersGlory") && Settings.NMGPatience && amount >= NAILSAGE_SOUL_REGEN + 1)
				amount = NAILSAGE_SOUL_REGEN + 1; //allow synergization but don't make it overpowered
			return amount;
		}

		private HitInstance HitInstanceAdjust(Fsm owner, HitInstance hit)
		{
			if (hit.Source.transform.parent != null)
			{
				string ParentName = hit.Source.transform.parent.name; //note - for many attacks this will be null; be careful
				if (ParentName != null && ParentName == "Thorn Hit" && IsGlorified("ThornsOfAgony"))
				{
					hit.DamageDealt = (int)(hit.DamageDealt * 1.5f);
					hit.AttackType = AttackTypes.Spell; //palace rose thorns are spell-type instead of normal-type
				}
			}
			if (hit.AttackType == AttackTypes.Spell)
			{
				if (WearingGlorifiedCharm("ShamanStone"))
					hit.DamageDealt = (int)(hit.DamageDealt * 1.125f);
				if (WearingGlorifiedCharm("SpellTwister"))
				{
					float DamageIncrease = PlayerData.instance.MPCharge / MONOMON_LENS_SOUL_PER_DAMAGE;
					if (DamageIncrease > MONOMON_LENS_MAX_INCREASE)
						DamageIncrease = MONOMON_LENS_MAX_INCREASE;
					DamageIncrease /= 100; //turn "25", "10" etc. into 0.25f, 0.1f
					DamageIncrease += 1f; //turn 0.25f, 0.1f etc. into 1.25f, 1.1f
					hit.DamageDealt = (int)(hit.DamageDealt * DamageIncrease);
				}
			}
			if (hit.AttackType == AttackTypes.Nail)
			{
				if (WearingGlorifiedCharm("FuryOfTheFallen") && PlayerData.instance.health == 1)
					hit.DamageDealt = (int)(hit.DamageDealt * 1.15f);
				if (WearingGlorifiedCharm("NailmastersGlory"))
				{
					if (Settings.NMGPatience) //change this AFTER modifying spell damage to avoid massive damage stacking
						hit.AttackType = AttackTypes.Spell;
					else
						hit.DamageDealt += (int)(hit.DamageDealt * 0.03f * (PlayerData.instance.maxHealth - PlayerData.instance.health));
				}
			}
			if (hit.AttackType == AttackTypes.SharpShadow && WearingGlorifiedCharm("SharpShadow"))
				hit.DamageDealt *= 2;
			return hit;
		}

		private bool DashPressed()
		{
			IEnumerator RazorShadow()
			{
				while (HeroController.instance.cState.shadowDashing)
					yield return null;
				PlayerData.instance.isInvincible = true;
				((SpriteFlash)SpriteField.GetValue(HeroController.instance)).flash(Color.black, 1.11f, 0.1f, 0.8f, 0.2f);
				yield return new WaitForSeconds(0.6f);
				PlayerData.instance.isInvincible = false;
			}
			float cooldown = (float)ShadowDashCD.GetValue(HeroController.instance);
			if (WearingGlorifiedCharm("SharpShadow") && cooldown <= 0)
				HeroController.instance.StartCoroutine(RazorShadow());
			return false;
		}

		private bool InInventory()
		{
			GameObject gameObject = GameObject.FindGameObjectWithTag("Inventory Top");
			if (gameObject == null)
				return false;
			PlayMakerFSM component = FSMUtility.LocateFSM(gameObject, "Inventory Control");
			if (component == null)
				return false;
			FsmBool fsmBool = component.FsmVariables.GetFsmBool("Open");
			return fsmBool != null && fsmBool.Value;
		}

		private IEnumerator ChangeSprites()
		{
			while (CharmIconList.Instance == null || GameManager.instance == null || HeroController.instance == null)
				yield return null;
			if (CachedSprites.Count == 0)
				foreach (int i in CharmNums) //num num =^.^=
					CachedSprites.Add(i.ToString(), CharmIconList.Instance.spriteList[i]);
			foreach (int i in CharmNums) //okay I want to die after writing that first comment
				CharmIconList.Instance.spriteList[i] = IsGlorified(i.ToString()) ? Sprites["Exaltation.Resources.Charms." + i + ".png"] : CachedSprites[i.ToString()];
			if (IsGlorified("FuryOfTheFallen") && Settings.FotFShade) //FotF has unique variants
				CharmIconList.Instance.spriteList[6] = Sprites["Exaltation.Resources.Charms.6_shade.png"];
			if (IsGlorified("NailmastersGlory") && Settings.NMGPatience) //and NMG is different entirely if made with the kingsoul
				CharmIconList.Instance.spriteList[26] = Sprites["Exaltation.Resources.Charms.26_patience.png"];
		}

		private IEnumerator RevertSprites()
		{
            while (CharmIconList.Instance == null || GameManager.instance == null || HeroController.instance == null)
                yield return null;
            if (CachedSprites.Count != 0)
                foreach (int i in CharmNums)
					CharmIconList.Instance.spriteList[i] = CachedSprites[i.ToString()];
        }

		private void MakeCanvas()
		{
			if (CanvasObject == null)
				CanvasObject = CanvasUtil.CreateCanvas(RenderMode.ScreenSpaceOverlay, new Vector2(1920f, 1080f));

			if (WyrmfuryIcon == null && HeroController.instance != null && WearingGlorifiedCharm("FuryOfTheFallen"))
			{
				WyrmfuryIcon = CanvasUtil.CreateImagePanel(CanvasObject,
					Sprites["Exaltation.Resources.WyrmfuryIcon.png"],
					new CanvasUtil.RectData(new Vector2(50f, 50f), new Vector2(0.13f, 0.78f),
					new Vector2(0.13f, 0.78f), new Vector2(0.13f, 0.78f)));

				Image WyrmfuryPicture = WyrmfuryIcon.GetComponent<Image>();

				WyrmfuryPicture.preserveAspect = false;
				WyrmfuryPicture.type = Image.Type.Filled;
				WyrmfuryPicture.fillMethod = Image.FillMethod.Horizontal;
				WyrmfuryPicture.fillAmount = 1f;
			}

			if (TextCanvas == null)
			{
				TextCanvas = CanvasUtil.CreateCanvas(RenderMode.ScreenSpaceOverlay, new Vector2(1920f, 1080f));
				GameObject TextPanel = CanvasUtil.CreateTextPanel(TextCanvas, "", 27, TextAnchor.MiddleCenter,
					new CanvasUtil.RectData(
						new Vector2(0, 50),
						new Vector2(0, 45),
						new Vector2(0, 0),
						new Vector2(1, 0),
						new Vector2(0.5f, 0.5f)));
				TextObject = TextPanel.GetComponent<Text>();
				TextObject.font = CanvasUtil.TrajanBold;
				TextObject.text = "";
				TextObject.fontSize = 42;
			}
		}

		private void UpdateWyrmfuryIcon()
		{
			if (WyrmfuryIcon == null)
				return;
			Image WyrmfuryPicture = WyrmfuryIcon.GetComponent<Image>();
			if (!WearingGlorifiedCharm("FuryOfTheFallen") || GameManager.instance == null || GameManager.instance.gameState != GameState.PLAYING || InInventory())
			{
				GameManager.instance.StartCoroutine(CanvasUtil.FadeOutCanvasGroup(CanvasObject.GetComponent<CanvasGroup>()));
				return;
			}
			if (CanvasObject.GetComponent<CanvasGroup>().gameObject.activeSelf == false)
			{
				GameManager.instance.StartCoroutine(CanvasUtil.FadeInCanvasGroup(CanvasObject.GetComponent<CanvasGroup>()));
				WyrmfuryPicture.fillAmount = 1f;
			}
			string Wyrm = !Settings.FotFShade ? "Wyrm" : "Shade";
			string Broken = WyrmfuryDeathProtection ? "Icon" : "Broken";
			WyrmfuryPicture.sprite = Sprites["Exaltation.Resources." + Wyrm + "fury" + Broken + ".png"];
		}

		private bool IsGlorified(string CharmName) //quick function for checking if a charm is glorified
		{
			if (DebugNoGlorification)
				return false;
			if (DebugAllGlories)
				return true;
			CharmName = CharmName.ToLower(); //prevent case entry from changing it up
			switch (CharmName)
			{
				case "gatheringswarm":
				case "1":
					return Settings.GatheringSwarmGlorified;
				case "waywardcompass":
				case "2":
					return Settings.WaywardCompassGlorified;
				case "grubsong":
				case "3":
					return Settings.GrubsongGlorified;
				case "stalwartshell":
				case "4":
					return Settings.StalwartShellGlorified;
				case "baldurshell":
				case "5":
					return Settings.BaldurShellGlorified;
				case "furyofthefallen":
				case "6":
					return Settings.FuryOfTheFallenGlorified;
				case "quickfocus":
				case "7":
					return Settings.QuickFocusGlorified;
				case "lifebloodheart":
				case "8":
					return Settings.LifebloodHeartGlorified;
				case "lifebloodcore":
				case "9":
					return Settings.LifebloodCoreGlorified;
				case "thornsofagony":
				case "12":
					return Settings.ThornsOfAgonyGlorified;
				case "steadybody":
				case "14":
					return Settings.SteadyBodyGlorified;
				case "sharpshadow":
				case "16":
					return Settings.SharpShadowGlorified;
				case "shamanstone":
				case "19":
					return Settings.ShamanStoneGlorified;
				case "soulcatcher":
				case "20":
					return Settings.SoulCatcherGlorified;
				case "glowingwomb":
				case "22":
					return Settings.GlowingWombGlorified;
				case "souleater":
				case "21":
					return Settings.SoulEaterGlorified;
				case "nailmastersglory":
				case "26":
					return Settings.NailmastersGloryGlorified;
				case "jonisblessing":
				case "27":
					return Settings.JonisBlessingGlorified;
				case "hiveblood":
				case "29":
					return Settings.HivebloodGlorified;
				case "dashmaster":
				case "31":
					return Settings.DashmasterGlorified;
				case "quickslash":
				case "32":
					return Settings.QuickSlashGlorified;
				case "spelltwister":
				case "33":
					return Settings.SpellTwisterGlorified;
				case "sprintmaster":
				case "37":
					return Settings.SprintmasterGlorified;
			}
			return false;
		}

		private bool CanGlorify(string CharmName)
		{
			CharmName = CharmName.ToLower(); //prevent case entry from changing it up
			PlayerData pd = PlayerData.instance;
			if (DebugAllGlories)
				return true;
			if (DebugNoGlorification)
				return false;
			switch (CharmName)
			{
				case "gatheringswarm":
				case "1":
					return pd.killedNailBros;
				case "waywardcompass":
				case "2":
					return pd.killedNailBros;
				case "grubsong":
				case "3":
					return pd.bossDoorStateTier1.boundSoul || pd.statueStateCollector.completedTier2;
				case "stalwartshell":
				case "4":
					return pd.bossDoorStateTier1.boundShell || pd.statueStateFailedChampion.completedTier2;
				case "baldurshell":
				case "5":
					return pd.killedNailBros;
				case "furyofthefallen":
				case "6":
					return pd.killedNailBros;
				case "quickfocus":
				case "7":
					return pd.bossDoorStateTier4.boundSoul || pd.statueStateHollowKnight.completedTier3;
				case "lifebloodheart":
				case "8":
					return pd.killedPaintmaster;
				case "lifebloodcore":
				case "9":
					return pd.killedPaintmaster;
				case "thornsofagony":
				case "12":
					return pd.killedPaintmaster;
				case "steadybody":
				case "14":
					return pd.killedNailBros;
				case "sharpshadow":
				case "16":
					return pd.bossDoorStateTier3.boundNail || pd.statueStateNosk.completedTier2;
				case "shamanstone":
				case "19":
					return pd.bossDoorStateTier3.boundShell || pd.statueStateSoulTyrant.completedTier2;
				case "soulcatcher":
				case "20":
					return pd.killedNailsage;
				case "souleater":
				case "21":
					return pd.bossDoorStateTier3.boundSoul || pd.statueStateSly.completedTier2;
				case "glowingwomb":
				case "22":
					return pd.killedPaintmaster;
				case "nailmastersglory":
				case "26":
					return pd.killedNailsage;
				case "jonisblessing":
				case "27":
					return pd.bossDoorStateTier3.boundShell || pd.statueStateSly.completedTier3;
				case "hiveblood":
				case "29":
					return pd.killedHollowKnightPrime;
				case "dashmaster":
				case "31":
					return pd.killedPaintmaster;
				case "quickslash":
				case "32":
					return pd.bossDoorStateTier4.boundNail || pd.statueStateNailmasters.completedTier3;
				case "spelltwister":
				case "33":
					return pd.bossDoorStateTier2.boundSoul || pd.statueStateUumuu.completedTier2;
				case "sprintmaster":
				case "37":
					return pd.killedPaintmaster;
			}
			return false;
		}

		private void GlorifyCharm(string CharmName, bool GloryAdjust = true)
		{
			CharmName = CharmName.ToLower();
			switch (CharmName)
			{
				case "gatheringswarm":
				case "1":
					Settings.GatheringSwarmGlorified = GloryAdjust;
					break;
				case "waywardcompass":
				case "2":
					Settings.WaywardCompassGlorified = GloryAdjust;
					break;
				case "grubsong":
				case "3":
					Settings.GrubsongGlorified = GloryAdjust;
					break;
				case "stalwartshell":
				case "4":
					Settings.StalwartShellGlorified = GloryAdjust;
					break;
				case "baldurshell":
				case "5":
					Settings.BaldurShellGlorified = GloryAdjust;
					break;
				case "furyofthefallen":
				case "6":
					Settings.FuryOfTheFallenGlorified = GloryAdjust;
					if (GloryAdjust && PlayerData.instance.gotShadeCharm)
						Settings.FotFShade = true;
					break;
				case "quickfocus":
				case "7":
					Settings.QuickFocusGlorified = GloryAdjust;
					break;
				case "lifebloodheart":
				case "8":
					Settings.LifebloodHeartGlorified = GloryAdjust;
					break;
				case "lifebloodcore":
				case "9":
					Settings.LifebloodCoreGlorified = GloryAdjust;
					break;
				case "thornsofagony":
				case "12":
					Settings.ThornsOfAgonyGlorified = GloryAdjust;
					break;
				case "steadybody":
				case "14":
					Settings.SteadyBodyGlorified = GloryAdjust;
					break;
				case "sharpshadow":
				case "16":
					Settings.SharpShadowGlorified = GloryAdjust;
					break;
				case "shamanstone":
				case "19":
					Settings.ShamanStoneGlorified = GloryAdjust;
					break;
				case "soulcatcher":
				case "20":
					Settings.SoulCatcherGlorified = GloryAdjust;
					break;
				case "souleater":
				case "21":
					Settings.SoulEaterGlorified = GloryAdjust;
					break;
				case "glowingwomb":
				case "22":
					Settings.GlowingWombGlorified = GloryAdjust;
					break;
				case "nailmastersglory":
				case "26":
					Settings.NailmastersGloryGlorified = GloryAdjust;
					break;
				case "jonisblessing":
				case "27":
					Settings.JonisBlessingGlorified = GloryAdjust;
					break;
				case "hiveblood":
				case "29":
					Settings.HivebloodGlorified = GloryAdjust;
					break;
				case "dashmaster":
				case "31":
					Settings.DashmasterGlorified = GloryAdjust;
					break;
				case "quickslash":
				case "32":
					Settings.QuickSlashGlorified = GloryAdjust;
					break;
				case "spelltwister":
				case "33":
					Settings.SpellTwisterGlorified = GloryAdjust;
					break;
				case "sprintmaster":
				case "37":
					Settings.SprintmasterGlorified = GloryAdjust;
					break;
			}
		}

		private bool WearingGlorifiedCharm(string CharmName) //sister function to IsGlorified to check if the player is wearing it
		{
			CharmName = CharmName.ToLower();
			bool glory = IsGlorified(CharmName);
			switch (CharmName)
			{
				case "gatheringswarm":
					return glory && PlayerData.instance.equippedCharm_1;
				case "waywardcompass":
					return glory && PlayerData.instance.equippedCharm_2;
				case "grubsong":
					return glory && PlayerData.instance.equippedCharm_3;
				case "stalwartshell":
					return glory && PlayerData.instance.equippedCharm_4;
				case "baldurshell":
					return glory && PlayerData.instance.equippedCharm_5;
				case "furyofthefallen":
					return glory && PlayerData.instance.equippedCharm_6;
				case "quickfocus":
					return glory && PlayerData.instance.equippedCharm_7;
				case "lifebloodheart":
					return glory && PlayerData.instance.equippedCharm_8;
				case "lifebloodcore":
					return glory && PlayerData.instance.equippedCharm_9;
				case "thornsofagony":
					return glory && PlayerData.instance.equippedCharm_12;
				case "steadybody":
					return glory && PlayerData.instance.equippedCharm_14;
				case "sharpshadow":
					return glory && PlayerData.instance.equippedCharm_16;
				case "shamanstone":
					return glory && PlayerData.instance.equippedCharm_19;
				case "soulcatcher":
					return glory && PlayerData.instance.equippedCharm_20;
				case "souleater":
					return glory && PlayerData.instance.equippedCharm_21;
				case "glowingwomb":
					return glory && PlayerData.instance.equippedCharm_22;
				case "nailmastersglory":
					return glory && PlayerData.instance.equippedCharm_26;
				case "jonisblessing":
					return glory && PlayerData.instance.equippedCharm_27;
				case "hiveblood":
					return glory && PlayerData.instance.equippedCharm_29;
				case "dashmaster":
					return glory && PlayerData.instance.equippedCharm_31;
				case "quickslash":
					return glory && PlayerData.instance.equippedCharm_32;
				case "spelltwister":
					return glory && PlayerData.instance.equippedCharm_33;
				case "sprintmaster":
					return glory && PlayerData.instance.equippedCharm_37;
			}
			return false;
		}

		public override void Initialize()
		{
			Instance = this;

			try { RegisterCallbacks(); }
			catch { Log("Exaltation failed to register callbacks!"); }
			try { LoadAssets.LoadSounds(); }
			catch { Log("Exaltation failed to find glorify sound!"); }
		}

		private IEnumerator GloryEffects(string glorytext)
		{
			if (Random.Range(1, 101) == 100)
				glorytext = "Charms glorified by the gods of discord";
			yield return new WaitForSeconds(0.35f);
			TextObject.text = glorytext;
			TextObject.CrossFadeAlpha(1f, 0f, false);
			((SpriteFlash)SpriteField.GetValue(HeroController.instance)).flash(Color.white, 1.75f, 0.25f, 1f, 0.5f);
			ReflectionHelper.GetField<HeroController, AudioSource>(HeroController.instance, "audioSource")
				.PlayOneShot(LoadAssets.GlorifySound, 1f);
			GameCameras.instance.cameraShakeFSM.SendEvent("BigShake");
			yield return new WaitForSeconds(1.5f);
			TextObject.CrossFadeAlpha(0f, 1f, false);
		}

		private void RegisterCallbacks()
		{
			ModHooks.HeroUpdateHook += OnHeroUpdate;
			ModHooks.LanguageGetHook += LanguageGet;

			ModHooks.BeforeAddHealthHook += TakeDamage;
			ModHooks.TakeHealthHook += TakeDamage;
			ModHooks.BlueHealthHook += LifebloodMasksRestored;

			ModHooks.CharmUpdateHook += OnCharmUpdate;

			ModHooks.SoulGainHook += GainSoul;

			ModHooks.HitInstanceHook += HitInstanceAdjust;

			ModHooks.DashPressedHook += DashPressed;

			ModHooks.BeforeSavegameSaveHook += BeforeSaveGameSave;
			ModHooks.AfterSavegameLoadHook += AfterSaveGameLoad;
			ModHooks.SavegameSaveHook += SaveGameSave;

			Assembly asm = Assembly.GetExecutingAssembly();
			Sprites = new Dictionary<string, Sprite>();
			CachedSprites = new Dictionary<string, Sprite>();
			foreach (string res in asm.GetManifestResourceNames())
			{
				if (!res.EndsWith(".png"))
					continue;

				using (Stream s = asm.GetManifestResourceStream(res))
				{
					if (s == null) continue;
					byte[] buffer = new byte[s.Length];
					s.Read(buffer, 0, buffer.Length);
					s.Dispose();

					//Create texture from bytes
					Texture2D tex = new Texture2D(1, 1);
					tex.LoadImage(buffer);

					//Create sprite from texture
					Sprites.Add(res, Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f)));
				}
			}

			On.GeoControl.OnEnable += ProcessGeoUpdate;
		}

		public void Unload()
		{
			ModHooks.HeroUpdateHook -= OnHeroUpdate;
			ModHooks.LanguageGetHook -= LanguageGet;

			ModHooks.BeforeAddHealthHook -= TakeDamage;
			ModHooks.TakeHealthHook -= TakeDamage;
			ModHooks.BlueHealthHook -= LifebloodMasksRestored;

			ModHooks.CharmUpdateHook -= OnCharmUpdate;

			ModHooks.SoulGainHook -= GainSoul;

			ModHooks.HitInstanceHook -= HitInstanceAdjust;

			ModHooks.DashPressedHook -= DashPressed;

			ModHooks.BeforeSavegameSaveHook -= BeforeSaveGameSave;
			ModHooks.AfterSavegameLoadHook -= AfterSaveGameLoad;
			ModHooks.SavegameSaveHook -= SaveGameSave;

            if (AlterSprites != null)
                GameManager.instance.StopCoroutine(AlterSprites);
            AlterSprites = GameManager.instance.StartCoroutine(RevertSprites());

            On.GeoControl.OnEnable -= ProcessGeoUpdate;
		}

		private void AdjustOldValues()
		{
			string version = GetVersion();
			if (version != Settings.LastVersion) //assume this to be an old version and adjust values accordingly
			{
				if (PlayerData.instance.focusMP_amount == 24) //1.0.2.1: Swift Focus focus cost decrease removed
					PlayerData.instance.focusMP_amount = 33;
				Settings.DreamWielderGlorified = false; //1.0.4.4: Dream Catcher removed
			}
		}
	}
}
