using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CSE377.Gameplay
{
    public enum RTSSoundType
    {
        Click,
        UnitSelect,
        UnitSelectSpearman,
        UnitSelectArcher,
        UnitSelectCavalry,
        UnitSelectVillager,
        UnitSelectTrebuchet,
        BuildingSelect,
        MoveOrder,
        MoveOrderCavalry,
        AttackOrder,
        WarHorn,
        SwordClash,
        ArrowShoot,
        ArrowHit,
        SiegeLaunch,
        SiegeImpact,
        BuildingCollapse,
        HarvestChop,
        HammerBuild,
        GoldMine,
        TechComplete,
        AlarmHorn,
        VictoryFanfare,
        DefeatSound
    }

    [System.Serializable]
    public class RTSMusicTrackInfo
    {
        public string id;
        public string title;
        public string subtitle;
        public string fileName;
        [System.NonSerialized] public AudioClip clip;
    }

    /// <summary>
    /// Harici dosya bağımlılığı olmadan saf matematiksel dalga formlarıyla
    /// gerçekçi ortaçağ RTS ses efektleri ve dinamik arka plan orkestral müzikleri
    /// üreten gelişmiş ses motoru. Ayrıca internetten indirilen gerçek ortaçağ
    /// orkestral müziklerini (MP3/OGG) disk ve Resources üzerinden oynatmayı destekler.
    /// </summary>
    public class RTSAudioManager : MonoBehaviour
    {
        public static RTSAudioManager Instance { get; private set; }

        [Header("Ses Seviyeleri")]
        [Range(0f, 1f)] public float masterVolume = 0.85f;
        [Range(0f, 1f)] public float sfxVolume = 0.85f;
        [Range(0f, 1f)] public float musicVolume = 0.40f;
        [Range(0f, 1f)] public float voiceVolume = 0.95f;
        public bool isMuted = false;

        [Header("Müzik Parçaları ve Seçim")]
        public int currentTrackIndex = 0;
        public List<RTSMusicTrackInfo> musicTracks = new List<RTSMusicTrackInfo>
        {
            new RTSMusicTrackInfo {
                id = "inn",
                title = "1. Han ve Köy Ezgisi",
                subtitle = "Huzurlu Köy, İnşaat & Ekonomi Ezgisi (The Old Tower Inn)",
                fileName = "medieval_inn.mp3"
            },
            new RTSMusicTrackInfo {
                id = "feast",
                title = "2. Kraliyet Şöleni",
                subtitle = "Saray Marşı, Gayda & Zafer Ezgisi (King's Feast)",
                fileName = "kings_feast.mp3"
            },
            new RTSMusicTrackInfo {
                id = "market",
                title = "3. Pazar Yeri & Ticaret",
                subtitle = "Canlı Ortaçağ Şehir & Ticaret Ritmi (Market Day)",
                fileName = "market_day.mp3"
            },
            new RTSMusicTrackInfo {
                id = "battle",
                title = "4. Meydan Muharebesi",
                subtitle = "Destansı Savaş Borazanları & Taarruz (It's Our Battle)",
                fileName = "its_our_battle.mp3"
            },
            new RTSMusicTrackInfo {
                id = "procedural",
                title = "5. Prosedürel Ezgi (Klasik)",
                subtitle = "Orijinal Matematiksel Sentezli Arka Plan Ezgisi",
                fileName = ""
            }
        };

        private AudioSource sfxSource;
        private AudioSource musicSource;
        private AudioSource voiceSource;
        private Dictionary<RTSSoundType, AudioClip> soundClips = new Dictionary<RTSSoundType, AudioClip>();
        private Dictionary<string, AudioClip> cachedVoiceClips = new Dictionary<string, AudioClip>();
        private Dictionary<string, float> lastVoiceTimes = new Dictionary<string, float>();

        private AudioClip peacefulMusicTrack;
        private AudioClip battleMusicTrack;
        private float lastCombatTime = -999f;
        private float lastVoicePlayTime = -999f;
        private bool isPlayingBattleMusic = false;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }

            // Kullanıcının kayıtlı ses tercihlerini yükle
            masterVolume = PlayerPrefs.GetFloat("RTS_MasterVol", 0.85f);
            sfxVolume = PlayerPrefs.GetFloat("RTS_SfxVol", 0.85f);
            musicVolume = PlayerPrefs.GetFloat("RTS_MusicVol", 0.40f);
            voiceVolume = PlayerPrefs.GetFloat("RTS_VoiceVol", 0.95f);
            isMuted = PlayerPrefs.GetInt("RTS_Muted", 0) == 1;
            currentTrackIndex = PlayerPrefs.GetInt("RTS_MusicTrack", 0);
            if (currentTrackIndex < 0 || currentTrackIndex >= musicTracks.Count) currentTrackIndex = 0;

            AudioListener.volume = isMuted ? 0f : masterVolume;

            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;

            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.spatialBlend = 0f;
            musicSource.loop = true;

            voiceSource = gameObject.AddComponent<AudioSource>();
            voiceSource.playOnAwake = false;
            voiceSource.spatialBlend = 0f;

            GenerateProceduralAudioClips();
            GenerateProceduralSoundtracks();
            PreloadAllVoices();

            StartBackgroundMusic();
        }

        public void SetMasterVolume(float val)
        {
            masterVolume = Mathf.Clamp01(val);
            PlayerPrefs.SetFloat("RTS_MasterVol", masterVolume);
            if (!isMuted) AudioListener.volume = masterVolume;
            if (musicSource != null) musicSource.volume = isMuted ? 0f : (musicVolume * masterVolume);
        }

        public void SetMusicVolume(float val)
        {
            musicVolume = Mathf.Clamp01(val);
            PlayerPrefs.SetFloat("RTS_MusicVol", musicVolume);
            if (musicSource != null) musicSource.volume = isMuted ? 0f : (musicVolume * masterVolume);
        }

        public void SetSfxVolume(float val)
        {
            sfxVolume = Mathf.Clamp01(val);
            PlayerPrefs.SetFloat("RTS_SfxVol", sfxVolume);
        }

        public void SetVoiceVolume(float val)
        {
            voiceVolume = Mathf.Clamp01(val);
            PlayerPrefs.SetFloat("RTS_VoiceVol", voiceVolume);
            if (voiceSource != null) voiceSource.volume = voiceVolume * masterVolume;
        }

        public void SetMuted(bool muted)
        {
            isMuted = muted;
            PlayerPrefs.SetInt("RTS_Muted", isMuted ? 1 : 0);
            AudioListener.volume = isMuted ? 0f : masterVolume;
            if (musicSource != null) musicSource.volume = isMuted ? 0f : (musicVolume * masterVolume);
        }

        public void ToggleMute()
        {
            SetMuted(!isMuted);
        }

        public void ResetToDefaults()
        {
            SetMasterVolume(0.85f);
            SetMusicVolume(0.40f);
            SetSfxVolume(0.85f);
            SetVoiceVolume(0.95f);
            SetMuted(false);
        }

        private void PreloadAllVoices()
        {
            string[] voiceNames = new string[]
            {
                "spearman_select", "spearman_move", "spearman_attack",
                "archer_select", "archer_move", "archer_attack",
                "cavalry_select", "cavalry_move", "cavalry_attack",
                "villager_select", "villager_move", "villager_work", "villager_build",
                "trebuchet_select", "trebuchet_attack",
                "alert_attack", "victory"
            };

            foreach (var vName in voiceNames)
            {
                var clip = Resources.Load<AudioClip>($"Audio/Voices/{vName}");
                if (clip != null)
                {
                    if (clip.loadState == AudioDataLoadState.Unloaded)
                    {
                        clip.LoadAudioData();
                    }
                    cachedVoiceClips[vName] = clip;
                }
            }
        }

        private void Update()
        {
            HandleDynamicMusicTransitions();
        }

        /// <summary>
        /// Muharebe başladığında arka plan müziğini dinamik olarak savaş tamtamlarına geçirir.
        /// </summary>
        public void NotifyCombat()
        {
            lastCombatTime = Time.time;
        }

        private void StartBackgroundMusic()
        {
            SelectMusicTrack(currentTrackIndex);
            PreloadBattleMusic();
        }

        private void PreloadBattleMusic()
        {
            var bTrack = musicTracks.Find(t => t.id == "battle");
            if (bTrack != null)
            {
                StartCoroutine(LoadTrackRoutine(bTrack, (clip) =>
                {
                    if (clip != null) battleMusicTrack = clip;
                }));
            }
        }

        public void SelectMusicTrack(int index)
        {
            if (musicTracks == null || musicTracks.Count == 0) return;
            currentTrackIndex = (index % musicTracks.Count + musicTracks.Count) % musicTracks.Count;
            PlayerPrefs.SetInt("RTS_MusicTrack", currentTrackIndex);

            var track = musicTracks[currentTrackIndex];
            if (track.clip != null)
            {
                ApplyMusicClip(track.clip);
            }
            else if (string.IsNullOrEmpty(track.fileName))
            {
                ApplyMusicClip(GeneratePeacefulMedievalTheme("ProceduralTheme", 12.0f));
            }
            else
            {
                StartCoroutine(LoadTrackRoutine(track, (clip) => ApplyMusicClip(clip)));
            }
        }

        public void NextMusicTrack()
        {
            SelectMusicTrack(currentTrackIndex + 1);
        }

        public void PreviousMusicTrack()
        {
            SelectMusicTrack(currentTrackIndex - 1);
        }

        public RTSMusicTrackInfo GetCurrentTrackInfo()
        {
            if (musicTracks != null && currentTrackIndex >= 0 && currentTrackIndex < musicTracks.Count)
                return musicTracks[currentTrackIndex];
            return null;
        }

        private void ApplyMusicClip(AudioClip clip)
        {
            if (clip == null || musicSource == null) return;
            peacefulMusicTrack = clip;
            if (!isPlayingBattleMusic)
            {
                musicSource.clip = clip;
                musicSource.volume = isMuted ? 0f : (musicVolume * masterVolume);
                musicSource.Play();
            }
        }

        private IEnumerator LoadTrackRoutine(RTSMusicTrackInfo track, Action<AudioClip> onLoaded)
        {
            if (track == null) yield break;

            string baseName = System.IO.Path.GetFileNameWithoutExtension(track.fileName);
            if (!string.IsNullOrEmpty(baseName))
            {
                ResourceRequest req = Resources.LoadAsync<AudioClip>($"Audio/Music/{baseName}");
                yield return req;

                AudioClip clip = req.asset as AudioClip;
                if (clip == null)
                {
                    clip = Resources.Load<AudioClip>($"Audio/Music/{baseName}");
                }

                if (clip != null)
                {
                    track.clip = clip;
                    onLoaded?.Invoke(clip);
                    yield break;
                }
            }

            // Fallback: Prosedürel
            if (peacefulMusicTrack != null)
            {
                onLoaded?.Invoke(peacefulMusicTrack);
            }
        }

        private void HandleDynamicMusicTransitions()
        {
            if (musicSource == null) return;

            bool shouldBeInCombat = (Time.time - lastCombatTime < 7.0f);

            if (shouldBeInCombat && !isPlayingBattleMusic && battleMusicTrack != null)
            {
                // Savaşa geçiş: Müziği savaş ritmine çapraz geçiş yap
                isPlayingBattleMusic = true;
                float playbackTime = musicSource.time;
                musicSource.clip = battleMusicTrack;
                musicSource.time = playbackTime % battleMusicTrack.length;
                musicSource.Play();
            }
            else if (!shouldBeInCombat && isPlayingBattleMusic && peacefulMusicTrack != null)
            {
                // Barışa dönüş: Sakin ortaçağ ezgisine dön
                isPlayingBattleMusic = false;
                float playbackTime = musicSource.time;
                musicSource.clip = peacefulMusicTrack;
                musicSource.time = playbackTime % peacefulMusicTrack.length;
                musicSource.Play();
            }

            float targetVol = isMuted ? 0f : (musicVolume * masterVolume);
            musicSource.volume = Mathf.Lerp(musicSource.volume, targetVol, Time.unscaledDeltaTime * 3f);
        }

        /// <summary>
        /// Savaş seslerini ve çatışma müziğini sıfırlar, barış moduna döndürür.
        /// </summary>
        public void ResetCombatAudio()
        {
            lastCombatTime = -999f;
            if (isPlayingBattleMusic && peacefulMusicTrack != null && musicSource != null)
            {
                isPlayingBattleMusic = false;
                musicSource.clip = peacefulMusicTrack;
                musicSource.Play();
            }
            if (voiceSource != null) voiceSource.Stop();
            if (sfxSource != null) sfxSource.Stop();
        }

        public void PlaySound(RTSSoundType soundType, float pitchVariation = 0.06f)
        {
            if (soundClips.TryGetValue(soundType, out var clip) && clip != null && sfxSource != null)
            {
                sfxSource.pitch = 1.0f + UnityEngine.Random.Range(-pitchVariation, pitchVariation);
                sfxSource.PlayOneShot(clip, sfxVolume * masterVolume);
            }
        }

        /// <summary>
        /// Gerçek Türkçe seslendirilmiş asker ve komutan repliklerini (MP3) oynatır.
        /// </summary>
        public void PlayVoice(string voiceName, bool force = false)
        {
            if (string.IsNullOrEmpty(voiceName)) return;

            // Aynı ses klibinin çok hızlı arka arkaya oynatılıp boğulmasını önle (en az 1.3 saniye ara)
            float lastSpecificTime;
            if (lastVoiceTimes.TryGetValue(voiceName, out lastSpecificTime))
            {
                if (Time.time - lastSpecificTime < 1.3f) return;
            }

            // Genel ses kanalı için minimum 0.35s ara bırak (force == true ise sadece acil durumlar geçer)
            if (!force && Time.time - lastVoicePlayTime < 0.35f) return;

            lastVoicePlayTime = Time.time;
            lastVoiceTimes[voiceName] = Time.time;

            if (!cachedVoiceClips.TryGetValue(voiceName, out var clip) || clip == null)
            {
                clip = Resources.Load<AudioClip>($"Audio/Voices/{voiceName}");
                if (clip != null)
                {
                    if (clip.loadState == AudioDataLoadState.Unloaded)
                    {
                        clip.LoadAudioData();
                    }
                    cachedVoiceClips[voiceName] = clip;
                }
            }

            if (clip != null && voiceSource != null)
            {
                if (clip.loadState == AudioDataLoadState.Unloaded)
                {
                    clip.LoadAudioData();
                }
                voiceSource.Stop();
                voiceSource.volume = voiceVolume * masterVolume;
                voiceSource.pitch = UnityEngine.Random.Range(0.98f, 1.02f);
                voiceSource.PlayOneShot(clip);
            }
            else
            {
                Debug.LogWarning($"[RTSAudioManager] Ses klibi bulunamadı veya oynatılamadı: Audio/Voices/{voiceName}");
            }
        }

        /// <summary>
        /// Birim sınıfına (Mızrakçı, Okçu, Süvari, Köylü, Mancınık) ve eylemine uygun Türkçe sesi çalar.
        /// Saldırı (attack) emirleri önceliklidir ve force=true ile derhal oynatılır.
        /// </summary>
        public void PlayUnitVoice(string unitType, string action, bool force = false)
        {
            string t = (unitType ?? "spearman").ToLower();
            string prefix = "spearman";

            if (t == "archer" || t == "crossbowman") prefix = "archer";
            else if (t == "cavalry" || t == "scout") prefix = "cavalry";
            else if (t == "villager") prefix = "villager";
            else if (t == "trebuchet") prefix = "trebuchet";

            string act = action.ToLower();
            string clipName = $"{prefix}_{act}";

            if (prefix == "trebuchet" && act == "move") clipName = "trebuchet_select";
            if (prefix == "villager" && (act == "attack" || act == "gather" || act == "harvest")) clipName = "villager_work";
            if (prefix == "villager" && (act == "build" || act == "repair")) clipName = "villager_build";

            bool shouldForce = force || (act == "attack" || act == "taarruz");
            PlayVoice(clipName, shouldForce);
        }

        #region Prosedürel Ses Efektleri Sentezleyicisi

        private void GenerateProceduralAudioClips()
        {
            // Temel UI
            soundClips[RTSSoundType.Click] = GenerateToneClip("Click", 0.04f, (t, p) => Mathf.Sin(t * Mathf.PI * 2f * 880f) * (1f - p));

            // Sınıfa Özel Birlik Seçim Sesleri (AoE Tarzı)
            soundClips[RTSSoundType.UnitSelect] = GenerateToneClip("UnitSelect", 0.08f, (t, p) => Mathf.Sin(t * Mathf.PI * 2f * 650f) * Mathf.Exp(-p * 6f));
            soundClips[RTSSoundType.UnitSelectSpearman] = GenerateArmorRustleClip("SpearmanSelect", 0.22f);
            soundClips[RTSSoundType.UnitSelectArcher] = GenerateBowTwangClip("ArcherSelect", 0.18f);
            soundClips[RTSSoundType.UnitSelectCavalry] = GenerateHorseWhinnyClip("CavalrySelect", 0.45f);
            soundClips[RTSSoundType.UnitSelectVillager] = GenerateToolKnockClip("VillagerSelect", 0.16f);
            soundClips[RTSSoundType.UnitSelectTrebuchet] = GenerateSiegeCreakClip("TrebuchetSelect", 0.38f);
            soundClips[RTSSoundType.BuildingSelect] = GenerateToneClip("BuildingSelect", 0.16f, (t, p) => (Mathf.Sin(t * Mathf.PI * 2f * 440f) * 0.7f + Mathf.Sin(t * Mathf.PI * 2f * 660f) * 0.4f) * Mathf.Exp(-p * 8f));

            // Taktiksel Emir Sesleri
            soundClips[RTSSoundType.MoveOrder] = GenerateBugleMarchClip("MoveOrder", 0.28f);
            soundClips[RTSSoundType.MoveOrderCavalry] = GenerateCavalryChargeClip("MoveOrderCavalry", 0.35f);
            soundClips[RTSSoundType.AttackOrder] = GenerateAggressiveTrumpetClip("AttackOrder", 0.32f);
            soundClips[RTSSoundType.WarHorn] = GenerateWarHornClip("WarHorn", 0.95f, 110f);

            // Muharebe ve Vuruş Efektleri
            soundClips[RTSSoundType.SwordClash] = GenerateMetallicClashClip("SwordClash", 0.22f);
            soundClips[RTSSoundType.ArrowShoot] = GenerateChirpClip("ArrowShoot", 0.12f, 1300f, 320f);
            soundClips[RTSSoundType.ArrowHit] = GenerateThumpClip("ArrowHit", 0.09f, 260f);
            soundClips[RTSSoundType.SiegeLaunch] = GenerateSiegeLaunchClip("SiegeLaunch", 0.40f);
            soundClips[RTSSoundType.SiegeImpact] = GenerateSiegeImpactClip("SiegeImpact", 0.55f);
            soundClips[RTSSoundType.BuildingCollapse] = GenerateRumbleDebrisClip("BuildingCollapse", 1.2f);

            // Ekonomi ve Zanaat Sesleri
            soundClips[RTSSoundType.HarvestChop] = GenerateThumpClip("HarvestChop", 0.12f, 140f);
            soundClips[RTSSoundType.HammerBuild] = GenerateMetalTapClip("HammerBuild", 0.14f);
            soundClips[RTSSoundType.GoldMine] = GeneratePickaxeGoldClip("GoldMine", 0.25f);
            soundClips[RTSSoundType.TechComplete] = GenerateBellChimeClip("TechComplete", 0.85f, 1046.5f);
            soundClips[RTSSoundType.AlarmHorn] = GenerateDualHornClip("AlarmHorn", 0.75f, 440f, 415.3f);
            soundClips[RTSSoundType.VictoryFanfare] = GenerateRoyalFanfareClip("VictoryFanfare", 2.2f);
            soundClips[RTSSoundType.DefeatSound] = GenerateDefeatMinorChord("DefeatSound", 1.8f);
        }

        private AudioClip GenerateToneClip(string name, float duration, Func<float, float, float> sampleFunc)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float progress = (float)i / totalSamples;
                data[i] = Mathf.Clamp(sampleFunc(t, progress), -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateChirpClip(string name, float duration, float startFreq, float endFreq)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];
            float phase = 0f;

            for (int i = 0; i < totalSamples; i++)
            {
                float p = (float)i / totalSamples;
                float freq = Mathf.Lerp(startFreq, endFreq, p);
                phase += freq / sampleRate;
                float envelope = Mathf.Sin(p * Mathf.PI);
                data[i] = Mathf.Sin(phase * Mathf.PI * 2f) * envelope * 0.75f;
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateMetallicClashClip(string name, float duration)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float p = (float)i / totalSamples;

                // Ani çelik çarpma patlaması
                float burst = (UnityEngine.Random.value * 2f - 1f) * Mathf.Exp(-p * 28f);
                // Çift harmonik çelik çınlama
                float ring1 = Mathf.Sin(t * Mathf.PI * 2f * 1850f) * Mathf.Exp(-p * 9f);
                float ring2 = Mathf.Sin(t * Mathf.PI * 2f * 3240f) * Mathf.Exp(-p * 14f) * 0.5f;

                data[i] = Mathf.Clamp((burst * 0.6f + ring1 * 0.5f + ring2 * 0.35f), -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateArmorRustleClip(string name, float duration)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float p = (float)i / totalSamples;
                float mailJingle = Mathf.Sin(t * Mathf.PI * 2f * 2200f) * (UnityEngine.Random.value * 0.5f) * Mathf.Exp(-p * 12f);
                float shieldThump = Mathf.Sin(t * Mathf.PI * 2f * 180f) * Mathf.Exp(-p * 18f);
                data[i] = Mathf.Clamp(mailJingle * 0.6f + shieldThump * 0.8f, -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateBowTwangClip(string name, float duration)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float p = (float)i / totalSamples;
                float twang = Mathf.Sin(t * Mathf.PI * 2f * (480f + 120f * Mathf.Sin(t * 80f))) * Mathf.Exp(-p * 16f);
                float flutter = (UnityEngine.Random.value * 2f - 1f) * Mathf.Exp(-p * 25f) * 0.3f;
                data[i] = Mathf.Clamp(twang * 0.85f + flutter, -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateHorseWhinnyClip(string name, float duration)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float p = (float)i / totalSamples;

                // Çift toynak sekmesi (Clip-clop)
                float hoof1 = (p < 0.2f) ? Mathf.Sin((t) * Mathf.PI * 2f * 130f) * Mathf.Exp(-(p / 0.2f) * 12f) : 0f;
                float hoof2 = (p > 0.15f && p < 0.4f) ? Mathf.Sin((t - 0.08f) * Mathf.PI * 2f * 140f) * Mathf.Exp(-((p - 0.15f) / 0.25f) * 12f) : 0f;

                // Hafif at kişneme vibratosu
                float whinny = 0f;
                if (p > 0.15f)
                {
                    float vibrato = Mathf.Sin(t * 32f) * 120f;
                    whinny = Mathf.Sin(t * Mathf.PI * 2f * (750f + vibrato)) * Mathf.Sin((p - 0.15f) / 0.85f * Mathf.PI) * 0.35f;
                }

                data[i] = Mathf.Clamp(hoof1 * 0.7f + hoof2 * 0.7f + whinny, -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateToolKnockClip(string name, float duration)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float p = (float)i / totalSamples;
                float knock = Mathf.Sin(t * Mathf.PI * 2f * 340f) * Mathf.Exp(-p * 22f);
                data[i] = Mathf.Clamp(knock * 0.85f, -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateSiegeCreakClip(string name, float duration)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float p = (float)i / totalSamples;
                float creak = Mathf.Sin(t * Mathf.PI * 2f * (160f + Mathf.Sin(t * 45f) * 80f)) * (1f - p);
                data[i] = Mathf.Clamp(creak * 0.65f, -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateBugleMarchClip(string name, float duration)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            // G4 (392Hz) -> C5 (523.25Hz)
            float half = duration * 0.5f;

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float freq = (t < half) ? 392.0f : 523.25f;
                float subT = (t < half) ? t : (t - half);
                float pSub = subT / half;

                float env = (pSub < 0.1f) ? (pSub / 0.1f) : Mathf.Exp(-(pSub - 0.1f) * 4f);
                float wave = Mathf.Sin(subT * Mathf.PI * 2f * freq) 
                           + Mathf.Sin(subT * Mathf.PI * 2f * freq * 2f) * 0.45f
                           + Mathf.Sin(subT * Mathf.PI * 2f * freq * 3f) * 0.2f;

                data[i] = Mathf.Clamp(wave * env * 0.7f, -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateCavalryChargeClip(string name, float duration)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float p = (float)i / totalSamples;

                // 3 hızlı nal vuruşu
                float gallop = Mathf.Sin(t * 60f) * Mathf.Exp(-Mathf.Repeat(t * 12f, 1f) * 8f) * 0.5f;
                float horn = Mathf.Sin(t * Mathf.PI * 2f * 440f) * Mathf.Sin(p * Mathf.PI) * 0.5f;

                data[i] = Mathf.Clamp(gallop + horn, -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateAggressiveTrumpetClip(string name, float duration)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            // D4 (293.66) -> F4 (349.23) -> A4 (440.0)
            float noteDur = duration / 3f;

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                int nIdx = Mathf.Clamp(Mathf.FloorToInt(t / noteDur), 0, 2);
                float freq = (nIdx == 0) ? 293.66f : ((nIdx == 1) ? 349.23f : 440.0f);
                float nT = t - nIdx * noteDur;
                float pN = nT / noteDur;

                float env = (pN < 0.1f) ? (pN / 0.1f) : Mathf.Exp(-(pN - 0.1f) * 5f);
                float wave = Mathf.Sin(nT * Mathf.PI * 2f * freq)
                           + Mathf.Sin(nT * Mathf.PI * 2f * freq * 2f) * 0.5f
                           + Mathf.Sin(nT * Mathf.PI * 2f * freq * 3f) * 0.25f;

                data[i] = Mathf.Clamp(wave * env * 0.7f, -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateWarHornClip(string name, float duration, float baseFreq)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float p = (float)i / totalSamples;

                float env = (p < 0.2f) ? (p / 0.2f) : Mathf.Exp(-(p - 0.2f) * 2.2f);
                // Tok bronz savaş borusu harmonikleri
                float h1 = Mathf.Sin(t * Mathf.PI * 2f * baseFreq);
                float h2 = Mathf.Sin(t * Mathf.PI * 2f * baseFreq * 2f) * 0.65f;
                float h3 = Mathf.Sin(t * Mathf.PI * 2f * baseFreq * 3f) * 0.45f;
                float h4 = Mathf.Sin(t * Mathf.PI * 2f * baseFreq * 4f) * 0.25f;

                data[i] = Mathf.Clamp((h1 + h2 + h3 + h4) * env * 0.65f, -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateSiegeLaunchClip(string name, float duration)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float p = (float)i / totalSamples;
                float heavyDrop = Mathf.Sin(t * Mathf.PI * 2f * 75f) * Mathf.Exp(-p * 8f);
                float armWhip = Mathf.Sin(t * Mathf.PI * 2f * (600f - p * 400f)) * (1f - p) * 0.4f;
                data[i] = Mathf.Clamp(heavyDrop * 0.8f + armWhip, -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateSiegeImpactClip(string name, float duration)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float p = (float)i / totalSamples;

                // Derin sub-bass sarsıntı + taş kırılma gürültüsü
                float subBass = Mathf.Sin(t * Mathf.PI * 2f * 55f) * Mathf.Exp(-p * 5f);
                float stoneCrash = (UnityEngine.Random.value * 2f - 1f) * Mathf.Exp(-p * 10f) * 0.6f;

                data[i] = Mathf.Clamp(subBass * 0.85f + stoneCrash, -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateRumbleDebrisClip(string name, float duration)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float p = (float)i / totalSamples;

                float rumble = Mathf.Sin(t * Mathf.PI * 2f * 48f) * (1f - p);
                float debris = (UnityEngine.Random.value * 2f - 1f) * Mathf.Exp(-p * 3.5f) * 0.5f;

                data[i] = Mathf.Clamp(rumble * 0.6f + debris * 0.6f, -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateThumpClip(string name, float duration, float baseFreq)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float p = (float)i / totalSamples;
                float thump = Mathf.Sin(t * Mathf.PI * 2f * (baseFreq * (1f - p * 0.4f))) * Mathf.Exp(-p * 14f);
                float crack = (p < 0.04f) ? (UnityEngine.Random.value * 2f - 1f) * 0.35f : 0f;
                data[i] = Mathf.Clamp(thump * 0.85f + crack, -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateMetalTapClip(string name, float duration)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float p = (float)i / totalSamples;
                float ping = Mathf.Sin(t * Mathf.PI * 2f * 980f) * Mathf.Exp(-p * 18f);
                float woodBase = Mathf.Sin(t * Mathf.PI * 2f * 240f) * Mathf.Exp(-p * 24f) * 0.5f;
                data[i] = Mathf.Clamp(ping * 0.7f + woodBase, -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GeneratePickaxeGoldClip(string name, float duration)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float p = (float)i / totalSamples;
                // Kristal altın çınlaması (2400Hz + 4800Hz)
                float goldPing = (Mathf.Sin(t * Mathf.PI * 2f * 2400f) + Mathf.Sin(t * Mathf.PI * 2f * 4800f) * 0.4f) * Mathf.Exp(-p * 12f);
                float pickHit = Mathf.Sin(t * Mathf.PI * 2f * 320f) * Mathf.Exp(-p * 22f);
                data[i] = Mathf.Clamp(goldPing * 0.75f + pickHit * 0.5f, -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateBellChimeClip(string name, float duration, float rootFreq)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float p = (float)i / totalSamples;
                float h1 = Mathf.Sin(t * Mathf.PI * 2f * rootFreq) * Mathf.Exp(-p * 4.5f);
                float h2 = Mathf.Sin(t * Mathf.PI * 2f * (rootFreq * 1.5f)) * Mathf.Exp(-p * 6f) * 0.45f;
                float h3 = Mathf.Sin(t * Mathf.PI * 2f * (rootFreq * 2.0f)) * Mathf.Exp(-p * 8f) * 0.25f;
                data[i] = Mathf.Clamp((h1 + h2 + h3) * 0.75f, -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateDualHornClip(string name, float duration, float f1, float f2)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float p = (float)i / totalSamples;
                float env = (p < 0.15f) ? (p / 0.15f) : Mathf.Exp(-(p - 0.15f) * 3f);
                float w1 = Mathf.Sin(t * Mathf.PI * 2f * f1);
                float w2 = Mathf.Sin(t * Mathf.PI * 2f * f2);
                data[i] = Mathf.Clamp((w1 + w2) * env * 0.5f, -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateRoyalFanfareClip(string name, float duration)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            // C5, E5, G5, C6 görkemli kraliyet akoru
            float[] notes = new float[] { 523.25f, 659.25f, 783.99f, 1046.5f };
            float noteDur = duration / notes.Length;

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                int nIdx = Mathf.Clamp(Mathf.FloorToInt(t / noteDur), 0, notes.Length - 1);
                float nT = t - nIdx * noteDur;
                float pN = nT / noteDur;
                float freq = notes[nIdx];

                float env = (pN < 0.08f) ? (pN / 0.08f) : Mathf.Exp(-(pN - 0.08f) * 3.5f);
                float wave = Mathf.Sin(nT * Mathf.PI * 2f * freq)
                           + Mathf.Sin(nT * Mathf.PI * 2f * freq * 2f) * 0.45f
                           + Mathf.Sin(nT * Mathf.PI * 2f * freq * 3f) * 0.2f;

                data[i] = Mathf.Clamp(wave * env * 0.65f, -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateDefeatMinorChord(string name, float duration)
        {
            int sampleRate = 44100;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            // Hüzünlü A Minör Akoru: A2 (110Hz), C3 (130.8Hz), E3 (164.8Hz)
            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;
                float p = (float)i / totalSamples;
                float env = (1f - p);
                float wave = Mathf.Sin(t * Mathf.PI * 2f * 110f)
                           + Mathf.Sin(t * Mathf.PI * 2f * 130.81f)
                           + Mathf.Sin(t * Mathf.PI * 2f * 164.81f);

                data[i] = Mathf.Clamp(wave * env * 0.35f, -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        #endregion

        #region Prosedürel Dinamik Ortaçağ Müzik Motoru (Soundtrack Engine)

        private void GenerateProceduralSoundtracks()
        {
            // 1. Barış ve İmar Müziği (Dorian Modu Ortaçağ Lavta & Flüt Ezgisi - 12 Saniye Dikişsiz Loop)
            peacefulMusicTrack = GeneratePeacefulMedievalTheme("PeacefulMedievalTheme", 12.0f);

            // 2. Muharebe ve Taarruz Müziği (Savaş Davulları & Ritmik Gerilim - 8 Saniye Dikişsiz Loop)
            battleMusicTrack = GenerateBattleDrumsTheme("BattleDrumsTheme", 8.0f);
        }

        private AudioClip GeneratePeacefulMedievalTheme(string name, float duration)
        {
            int sampleRate = 22050; // Müzik için hafif ve optimize örnekleme hızı
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            // D Minör / Dorian Ortaçağ Melodisi (D4, F4, G4, A4, C5, D5)
            float[] chords = new float[] { 293.66f, 349.23f, 392.00f, 440.00f, 523.25f, 587.33f };

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;

                // Lavta Arpejleri (Lute Plucks - her 0.375 saniyede bir nota)
                float luteBeat = Mathf.Repeat(t, 0.375f);
                int luteNoteIndex = (Mathf.FloorToInt(t / 0.375f) * 2) % chords.Length;
                float luteFreq = chords[luteNoteIndex];
                float luteEnv = Mathf.Exp(-luteBeat * 9f);
                float luteWave = (Mathf.Sin(luteBeat * Mathf.PI * 2f * luteFreq) + Mathf.Sin(luteBeat * Mathf.PI * 2f * luteFreq * 2f) * 0.3f) * luteEnv * 0.28f;

                // Tahta Flüt Melodisi (Wooden Recorder - 2 saniyede bir sakin nota değişimi)
                int fluteNoteIndex = Mathf.FloorToInt(t / 2.0f) % chords.Length;
                float fluteFreq = chords[fluteNoteIndex] * 2.0f; // Üst oktav
                float fluteVibrato = Mathf.Sin(t * Mathf.PI * 2f * 5.0f) * 6.0f; // Doğal el vibratosu
                float fluteEnv = Mathf.Sin(Mathf.Repeat(t, 2.0f) / 2.0f * Mathf.PI);
                float fluteWave = Mathf.Sin(t * Mathf.PI * 2f * (fluteFreq + fluteVibrato)) * fluteEnv * 0.16f;

                // Kır Rüzgarı Ambiyansı (Subtle Wind Breeze)
                float breeze = (UnityEngine.Random.value * 2f - 1f) * 0.04f;

                data[i] = Mathf.Clamp(luteWave + fluteWave + breeze, -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateBattleDrumsTheme(string name, float duration)
        {
            int sampleRate = 22050;
            int totalSamples = Mathf.FloorToInt(duration * sampleRate);
            float[] data = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                float t = (float)i / sampleRate;

                // Ağır Savaş Davulu / Timpani (Her 0.5 saniyede bir güçlü vuruş)
                float drumBeat = Mathf.Repeat(t, 0.5f);
                float drumEnv = Mathf.Exp(-drumBeat * 14f);
                float drumWave = Mathf.Sin(drumBeat * Mathf.PI * 2f * 82f) * drumEnv * 0.65f;

                // Ritmik Marş Tamtamı (16'lık tempo tıkırtısı)
                float marchBeat = Mathf.Repeat(t, 0.125f);
                float marchEnv = Mathf.Exp(-marchBeat * 24f);
                float marchWave = (UnityEngine.Random.value * 2f - 1f) * marchEnv * 0.22f;

                // Alttan gelen gerilim bas tonu (Low D Drone)
                float bassDrone = Mathf.Sin(t * Mathf.PI * 2f * 73.4f) * 0.18f;

                data[i] = Mathf.Clamp(drumWave + marchWave + bassDrone, -1f, 1f);
            }

            var clip = AudioClip.Create(name, totalSamples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        #endregion
    }
}
