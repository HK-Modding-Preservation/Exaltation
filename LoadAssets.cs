using System.IO;
using System.Reflection;
using UnityEngine;

namespace Exaltation
{
	public static class LoadAssets
	{
		public static AudioClip GlorifySound;
		public static AudioClip WyrmfurySound;
		public static AudioClip ShellSound;
		public static AudioClip BlockerSound;
		public static AudioClip DreamCatcherSound;
		public static void LoadSounds()
		{
			foreach (string res in Assembly.GetExecutingAssembly().GetManifestResourceNames())
			{
				if (res.EndsWith(".wav"))
				{
					Modding.Logger.Log("Found wav sound \"" + res + "\".");
					Stream audio = Assembly.GetExecutingAssembly().GetManifestResourceStream(res);
					if (audio != null)
					{
						byte[] buffer = new byte[audio.Length];
						audio.Read(buffer, 0, buffer.Length);
						audio.Dispose();
						if (res.StartsWith("Exaltation.Resources.Glorify")) //I could technically remove the first two words here but I'm lazy.
						{
							GlorifySound = WavUtility.ToAudioClip(buffer);
							Modding.Logger.Log("Found glorify sound effect.");
						}
						else if (res.StartsWith("Exaltation.Resources.WyrmfuryProtect"))
						{
							WyrmfurySound = WavUtility.ToAudioClip(buffer);
							Modding.Logger.Log("Found Wyrmfury sound effect.");
						}
						else if (res.StartsWith("Exaltation.Resources.ShellProtect"))
						{
							ShellSound = WavUtility.ToAudioClip(buffer);
							Modding.Logger.Log("Found Kingsmould Carapace sound effect.");
						}
						else if (res.StartsWith("Exaltation.Resources.BlockerRegen"))
						{
							BlockerSound = WavUtility.ToAudioClip(buffer);
							Modding.Logger.Log("Found Baldur Stoneshell sound effect.");
						}
						else if (res.StartsWith("Exaltation.Resources.DreamCatcherProc"))
						{
							DreamCatcherSound = WavUtility.ToAudioClip(buffer);
							Modding.Logger.Log("Found Dream Catcher sound effect.");
						}
					}
				}
			}
		}
	}
}