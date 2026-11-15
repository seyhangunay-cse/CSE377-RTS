using UnityEngine;

namespace CSE377.Gameplay
{
    /// <summary>
    /// Klasik Age of Empires tarzı izometrik RTS kamerası.
    /// WASD veya ok tuşları ile haritada serbestçe gezinme ve fare tekeri ile yakınlaşma/uzaklaşma sağlar.
    /// </summary>
    public class RTSCameraController : MonoBehaviour
    {
        [Header("Hareket Ayarları")]
        public float panSpeed = 24f;
        public float fastPanSpeed = 46f;

        [Header("Fare Kenar Kaydırma (Edge Scrolling)")]
        public bool enableEdgeScrolling = true;
        public float edgeMargin = 16f;

        [Header("Zoom (Yakınlaşma) Ayarları")]
        public float zoomSpeed = 25f;
        public float minZoomY = 8f;
        public float maxZoomY = 55f;

        [Header("Harita Zemin Sınırları (Ground Look-At Bounds)")]
        public float mapGroundLimitX = 66f; // -66m ile +66m arası (Oynanabilir harita sınırı içinde tutar)
        public float mapGroundLimitZ = 66f; // -66m ile +66m arası

        // Geriye dönük uyumluluk için
        public Vector2 xLimits
        {
            get => new Vector2(-mapGroundLimitX, mapGroundLimitX);
            set => mapGroundLimitX = Mathf.Abs(value.y);
        }
        public Vector2 zLimits
        {
            get => new Vector2(-mapGroundLimitZ, mapGroundLimitZ);
            set => mapGroundLimitZ = Mathf.Abs(value.y);
        }

        private Vector3 targetPosition;

        private void Start()
        {
            targetPosition = transform.position;
            ClampTargetPosition();
        }

        private float shakeIntensity = 0f;
        private float shakeDuration = 0f;

        public void TriggerShake(float intensity = 0.45f, float duration = 0.35f)
        {
            shakeIntensity = intensity;
            shakeDuration = duration;
        }

        public void FocusOnPosition(Vector3 worldPos)
        {
            float currentY = transform.position.y;
            targetPosition = new Vector3(worldPos.x, currentY, worldPos.z - 20f);
            transform.position = targetPosition;
            ClampTargetPosition();
        }

        private void Update()
        {
            HandlePanInput();
            HandleZoomInput();

            // Boşluk (Spacebar) Kısayolu: Son saldırı/alarm noktasına veya Şehir Merkezine atla (Age of Empires Kısayolu)
            if (Input.GetKeyDown(KeyCode.Space) && !IsAnyInputFieldFocused())
            {
                if (Time.time - RTSMinimapController.LastAlertTime < 8.0f && RTSMinimapController.LastAlertWorldPosition != Vector3.zero)
                {
                    FocusOnWorldPosition(RTSMinimapController.LastAlertWorldPosition);
                    RTSHUDController.Instance?.SetAiStatus("[KAMERA] Son saldırı / alarm bölgesine odaklanıldı!", Color.yellow);
                    RTSAudioManager.Instance?.PlaySound(RTSSoundType.Click, 0.2f);
                }
                else
                {
                    RTSBuilding townCenter = null;
                    foreach (var b in RTSBuilding.AllBuildings)
                    {
                        if (b != null && b.team == UnitTeam.Player && b.buildingType == RTSBuildingType.TownCenter && b.currentHp > 0)
                        {
                            townCenter = b;
                            break;
                        }
                    }

                    if (townCenter != null)
                    {
                        FocusOnWorldPosition(townCenter.transform.position);
                        RTSHUDController.Instance?.SetAiStatus("[KAMERA] Şehir Merkezine odaklanıldı.", Color.cyan);
                        RTSAudioManager.Instance?.PlaySound(RTSSoundType.Click, 0.2f);
                    }
                }
            }
            
            // Yumuşak kamera takibi ve patlama sarsıntısı
            Vector3 targetWithShake = targetPosition;
            if (shakeDuration > 0f)
            {
                shakeDuration -= Time.deltaTime;
                targetWithShake += Random.insideUnitSphere * shakeIntensity;
            }
            transform.position = Vector3.Lerp(transform.position, targetWithShake, Time.deltaTime * 12f);
        }

        private void HandlePanInput()
        {
            // Eğer oyuncu chat kutusuna veya herhangi bir arayüz metin alanına yazı yazıyorsa kamera kaymasını engelle
            if (IsAnyInputFieldFocused()) return;

            float speed = Input.GetKey(KeyCode.LeftShift) ? fastPanSpeed : panSpeed;
            float h = Input.GetAxisRaw("Horizontal"); // A - D veya Sol - Sağ
            float v = Input.GetAxisRaw("Vertical");   // W - S veya Yukarı - Aşağı

            // Ekran kenarına fare yaklaştığında kaydırma (Edge Scrolling)
            if (enableEdgeScrolling && Application.isFocused)
            {
                Vector3 mousePos = Input.mousePosition;
                if (mousePos.x >= 0 && mousePos.x <= Screen.width && mousePos.y >= 0 && mousePos.y <= Screen.height)
                {
                    if (mousePos.x <= edgeMargin && h == 0) h = -1f;
                    else if (mousePos.x >= Screen.width - edgeMargin && h == 0) h = 1f;

                    if (mousePos.y <= edgeMargin && v == 0) v = -1f;
                    else if (mousePos.y >= Screen.height - edgeMargin && v == 0) v = 1f;
                }
            }

            Vector3 forward = transform.forward;
            forward.y = 0;
            forward.Normalize();

            Vector3 right = transform.right;
            right.y = 0;
            right.Normalize();

            Vector3 move = (forward * v + right * h).normalized * speed * Time.deltaTime;
            targetPosition += move;

            ClampTargetPosition();
        }

        private void HandleZoomInput()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f)
            {
                targetPosition += transform.forward * scroll * zoomSpeed * 10f;
                targetPosition.y = Mathf.Clamp(targetPosition.y, minZoomY, maxZoomY);
                ClampTargetPosition();
            }
        }

        /// <summary>
        /// Kameranın baktığı zemin noktasını (Ground Look-At) harita sınırları içinde tutar.
        /// Bu sayede kamera açısı ne olursa olsun haritanın güneyine tam inilebilir
        /// ve kuzeyde boşluk/sonsuzluk alanına taşma engellenir.
        /// </summary>
        private void ClampTargetPosition()
        {
            float pitchRad = Mathf.Max(20f, transform.eulerAngles.x) * Mathf.Deg2Rad;
            float zOffset = targetPosition.y / Mathf.Tan(pitchRad);

            float currentGroundX = targetPosition.x;
            float currentGroundZ = targetPosition.z + zOffset;

            float clampedGroundX = Mathf.Clamp(currentGroundX, -mapGroundLimitX, mapGroundLimitX);
            float clampedGroundZ = Mathf.Clamp(currentGroundZ, -mapGroundLimitZ, mapGroundLimitZ);

            targetPosition.x = clampedGroundX;
            targetPosition.z = clampedGroundZ - zOffset;
        }

        /// <summary>
        /// Mini-haritadan tıklanan dünya pozisyonuna kamerayı yumuşak bir şekilde odaklar.
        /// </summary>
        public void FocusOnWorldPosition(Vector3 worldPos)
        {
            float pitchRad = Mathf.Max(20f, transform.eulerAngles.x) * Mathf.Deg2Rad;
            float zOffset = targetPosition.y / Mathf.Tan(pitchRad);

            float targetGroundX = Mathf.Clamp(worldPos.x, -mapGroundLimitX, mapGroundLimitX);
            float targetGroundZ = Mathf.Clamp(worldPos.z, -mapGroundLimitZ, mapGroundLimitZ);

            targetPosition = new Vector3(
                targetGroundX,
                targetPosition.y,
                targetGroundZ - zOffset
            );
        }

        private bool IsAnyInputFieldFocused()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                var curSelected = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;
                if (curSelected != null)
                {
                    if (curSelected.GetComponent<UnityEngine.UI.InputField>() != null) return true;
                }
            }

            if (RTSHUDController.Instance != null && RTSHUDController.Instance.customPromptInput != null)
            {
                if (RTSHUDController.Instance.customPromptInput.isFocused) return true;
            }

            return false;
        }
    }
}
