using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class ArrowFollowTrajectory : NetworkBehaviour
{
    [Header("Ayarlar")]
    [SerializeField] public float arrowSpeed = 20f, delay;
    [SerializeField] private bool autoRotate = true;
    [SerializeField] private float rotationSmoothing = 10f;

    [Header("�arp��ma/Hasar/Saplanma")]
    [SerializeField] private float damage = 20f;
    [SerializeField] private float stickDuration = 3f;
    [SerializeField] private bool stickToHitTarget = true;
    [SerializeField] private float surfaceBackOffset = 0.02f;

    [Header("�arp��ma Ayarlar�")]
    [SerializeField] private LayerMask hitMask = ~0;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;
    [SerializeField] private bool useSphereCast = false;
    [SerializeField] private float sphereRadius = 0.05f;

    [Header("Efekt")]
    [SerializeField] private GameObject effectPrefab;
    private NetworkObject currentEffect;
    private readonly NetworkVariable<ulong> effectObjectId = new NetworkVariable<ulong>();

    [Header("G�rsel (Yerel)")]
    [SerializeField] private LineRenderer trajectoryLine; // Sadece yerel ni�an g�stergesi i�in

    [Header("A� Senkronu")]
    [SerializeField] private float interpolationTime = 0.1f;
    [SerializeField] private float netSyncRate = 30f; // saniyede ka� kez state yayal�m
    private float netSyncTimer;

    // A� de�i�kenleri (sunucu yayar, istemciler okur)
    private readonly NetworkVariable<Vector3> networkPosition = new NetworkVariable<Vector3>();
    private readonly NetworkVariable<Vector3> networkVelocity = new NetworkVariable<Vector3>();
    private readonly NetworkVariable<int> networkPointIndex = new NetworkVariable<int>();

    // Deterministik y�r�nge verileri (parametreler)
    private Vector3 initStart;
    private Vector3 initVelocity;
    private int initPointCount;
    private float initTimeStep;
    private float initGravityMul = 1f;

    // �retilmi� y�r�nge ve hareket durumu
    private Vector3[] trajectoryPoints;      // Y�r�nge noktalar�
    private int currentPointIndex = 0;       // Sunucu/yerel hedef nokta indeksi
    private bool isMoving = false;

    // �stemci taraf� tahmin (prediction)
    private Vector3 clientPredictedPosition;
    private Vector3 clientPredictedVelocity;
    private int clientPredictedIndex = 0;

    private void Awake()
    {
        enabled = false; // sadece ate� edildi�inde aktif
    }

    private void OnEnable()
    {
        if (IsClient)
        {
            networkPosition.OnValueChanged += OnPositionChanged;
            networkVelocity.OnValueChanged += OnVelocityChanged;
            networkPointIndex.OnValueChanged += OnPointIndexChanged;
        }
        effectObjectId.OnValueChanged += OnEffectObjectIdChanged;
        TryAssignEffect();
    }

    private void OnDisable()
    {
        if (IsClient)
        {
            networkPosition.OnValueChanged -= OnPositionChanged;
            networkVelocity.OnValueChanged -= OnVelocityChanged;
            networkPointIndex.OnValueChanged -= OnPointIndexChanged;
        }
        effectObjectId.OnValueChanged -= OnEffectObjectIdChanged;

        if (IsServer && currentEffect != null)
        {
            currentEffect.Despawn();
            currentEffect = null;
        }
    }

    // Mevcut API (sunucuda kullan�lmal�)
    public void StartFollowingTrajectory(LineRenderer lineRenderer)
    {
        if (!IsServer)
        {
            Debug.LogWarning("StartFollowingTrajectory(LineRenderer) sadece sunucuda kullan�lmal�d�r. Parametreli ba�latmay� kullan�n.");
            return;
        }

        if (lineRenderer == null || lineRenderer.positionCount < 2)
        {
            Debug.LogWarning("Ge�erli bir y�r�nge �izgisi bulunamad�!");
            return;
        }

        trajectoryPoints = new Vector3[lineRenderer.positionCount];
        lineRenderer.GetPositions(trajectoryPoints);

        // Varsay�lan parametreleri tahmin et
        initStart = trajectoryPoints[0];
        Vector3 firstDir = (trajectoryPoints[1] - trajectoryPoints[0]).normalized;
        initVelocity = firstDir * Mathf.Max(arrowSpeed, 0.01f);
        initPointCount = trajectoryPoints.Length;
        initTimeStep = 0.0333f;
        initGravityMul = 1f;

        ServerBeginMovementAndBroadcast();
    }

    // Tercih edilen ba�latma (sunucu taraf�nda deterministik parametrelerle)
    public void StartFollowingTrajectoryParams(Vector3 start, Vector3 launchVelocity, int points, float timeStep, float gravityMul)
    {
        if (!IsServer)
        {
            Debug.LogWarning("StartFollowingTrajectoryParams sadece sunucuda �a�r�lmal�d�r.");
            return;
        }

        initStart = start;
        initVelocity = launchVelocity;
        initPointCount = Mathf.Max(points, 2);
        initTimeStep = Mathf.Max(timeStep, 0.0001f);
        initGravityMul = Mathf.Max(gravityMul, 0f);

        trajectoryPoints = GenerateTrajectoryPoints(initStart, initVelocity, initPointCount, initTimeStep, initGravityMul);
        ServerBeginMovementAndBroadcast();
    }

    private void ServerBeginMovementAndBroadcast()
    {
        currentPointIndex = 1;
        transform.position = trajectoryPoints[0];
        isMoving = true;
        enabled = true;

        // �lk network state
        networkPosition.Value = transform.position;
        networkVelocity.Value = (trajectoryPoints[1] - trajectoryPoints[0]).normalized * arrowSpeed;
        networkPointIndex.Value = currentPointIndex;

        // Efekt: sunucuda spawn + id yay�nla
        if (effectPrefab != null)
        {
            currentEffect = NetworkProjectilePool.Singleton.GetNetworkObject(effectPrefab, transform.position, transform.rotation);
            currentEffect.Spawn(true);
            effectObjectId.Value = currentEffect.NetworkObjectId;
        }

        // �stemcilere parametre yay�n�
        InitClientsClientRpc(initStart, initVelocity, initPointCount, initTimeStep, initGravityMul, arrowSpeed, autoRotate, rotationSmoothing);
    }

    [ClientRpc]
    private void InitClientsClientRpc(
        Vector3 start,
        Vector3 launchVelocity,
        int points,
        float timeStep,
        float gravityMul,
        float speed,
        bool autoRot,
        float rotSmooth)
    {
        initStart = start;
        initVelocity = launchVelocity;
        initPointCount = Mathf.Max(points, 2);
        initTimeStep = Mathf.Max(timeStep, 0.0001f);
        initGravityMul = Mathf.Max(gravityMul, 0f);

        arrowSpeed = speed;
        autoRotate = autoRot;
        rotationSmoothing = rotSmooth;

        trajectoryPoints = GenerateTrajectoryPoints(initStart, initVelocity, initPointCount, initTimeStep, initGravityMul);

        clientPredictedIndex = 1;
        clientPredictedPosition = trajectoryPoints[0];
        clientPredictedVelocity = (trajectoryPoints[1] - trajectoryPoints[0]).normalized * arrowSpeed;

        transform.position = clientPredictedPosition;
        if (autoRotate) LookAtDirection(clientPredictedVelocity);

        isMoving = true;
        enabled = true;

        // Efekt id atanm��sa ba�lamay� dene
        TryAssignEffect();
    }

    private void FixedUpdate()
    {
        if (!isMoving || trajectoryPoints == null || trajectoryPoints.Length < 2)
            return;

        if (IsServer)
        {
            ServerMoveAlongTrajectory();
        }
        else if (IsClient)
        {
            PredictMovement();
            InterpolateToServerState();
        }

        // Efekt takip (her iki tarafta da)
        if (currentEffect != null)
            currentEffect.transform.SetPositionAndRotation(transform.position, transform.rotation);
    }

    // Sunucu otoritesi: y�r�ngede sabit h�zla ilerlet + �arp��ma/hasar/saplanma
    private void ServerMoveAlongTrajectory()
    {
        float remaining = arrowSpeed * Time.fixedDeltaTime;

        while (remaining > 0f && currentPointIndex < trajectoryPoints.Length)
        {
            Vector3 target = trajectoryPoints[currentPointIndex];
            Vector3 toTarget = target - transform.position;
            float dist = toTarget.magnitude;

            if (dist < 1e-5f)
            {
                currentPointIndex++;
                continue;
            }

            float step = Mathf.Min(remaining, dist);
            Vector3 dir = toTarget / dist;

            // �arp��ma kontrol� (ad�m kadar ray/sphere cast)
            if (CastForHit(transform.position, dir, step, out RaycastHit hit))
            {
                // �sabet konumu ve y�n
                Vector3 hitPos = hit.point - dir * surfaceBackOffset;
                transform.position = hitPos;
                if (autoRotate) LookAtDirection(dir * arrowSpeed);

                // A� durumunu kesin konuma it (an�nda)
                networkPosition.Value = transform.position;
                networkVelocity.Value = Vector3.zero;
                networkPointIndex.Value = currentPointIndex;

                // Hasar uygula (IDamagable varsa)
                if (hit.collider != null && hit.collider.TryGetComponent<IDamagable>(out var damagable))
                {
                    damagable.Damage(damage, hit.point);
                    if (hit.collider.TryGetComponent<NetworkObject>(out var targetNet))
                    {
                        NotifyDamageClientRpc(targetNet.NetworkObjectId, damage);
                    }
                }

                // Saplanma: hedefe yap��t�r (opsiyonel)
                ulong parentId = 0;
                bool hasParent = false;
                if (stickToHitTarget && hit.collider != null && hit.collider.TryGetComponent<NetworkObject>(out var parentNet))
                {
                    transform.SetParent(parentNet.transform, true);
                    hasParent = true;
                    parentId = parentNet.NetworkObjectId;
                }

                // Efekti oka ba�la ki saplan�nca birlikte kals�n
                if (currentEffect != null)
                    currentEffect.transform.SetParent(transform, true);

                // �stemcilere "stuck" durumu yay�nla
                StuckClientRpc(transform.position, transform.forward, hasParent, parentId);

                // Hareketi durdur
                isMoving = false;

                // Bir s�re bekleyip yok et
                StartCoroutine(DestroyAfterDelay(stickDuration));
                return;
            }

            // Hareket
            transform.position += dir * step;
            remaining -= step;

            // G�rsel y�n
            if (autoRotate) LookAtDirection(dir * arrowSpeed);

            // A� g�ncellemesini throttle et
            netSyncTimer += step; // kat edilen mesafe ile orant�l�; dilersen Time.fixedDeltaTime kullan
            if (netSyncTimer >= (arrowSpeed / Mathf.Max(arrowSpeed, 0.0001f)) * (1f / netSyncRate))
            {
                networkPosition.Value = transform.position;
                networkVelocity.Value = dir * arrowSpeed;
                networkPointIndex.Value = currentPointIndex;
                netSyncTimer = 0f;
            }

            if (step >= dist - 1e-6f)
                currentPointIndex++;
        }

        if (currentPointIndex >= trajectoryPoints.Length)
        {
            OnTrajectoryCompleted();
        }
    }

    private bool CastForHit(Vector3 origin, Vector3 dir, float distance, out RaycastHit hit)
    {
        if (useSphereCast)
            return Physics.SphereCast(origin, sphereRadius, dir, out hit, distance, hitMask, triggerInteraction);
        else
            return Physics.Raycast(origin, dir, out hit, distance, hitMask, triggerInteraction);
    }

    [ClientRpc]
    private void StuckClientRpc(Vector3 pos, Vector3 forward, bool hasParent, ulong parentId)
    {
        // �stemciler: sunucu konum/rotasyonuna atla ve tahmini durdur
        transform.SetPositionAndRotation(pos, Quaternion.LookRotation(forward, Vector3.up));
        isMoving = false;

        // Hedefe parentla (varsa)
        if (hasParent && parentId != 0 && NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(parentId, out var parentObj))
        {
            transform.SetParent(parentObj.transform, true);
        }

        // Efekti oka ba�la ki sabit kals�n
        if (currentEffect != null)
            currentEffect.transform.SetParent(transform, true);
    }

    private void OnEffectObjectIdChanged(ulong oldId, ulong newId)
    {
        TryAssignEffect();
    }

    private void TryAssignEffect()
    {
        if (IsClient && effectObjectId.Value != 0 && currentEffect == null)
        {
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(effectObjectId.Value, out NetworkObject netObj))
            {
                currentEffect = netObj;
                // �lk atamada poz/rot e�itle
                currentEffect.transform.SetPositionAndRotation(transform.position, transform.rotation);
            }
        }
    }

    [ClientRpc]
    private void NotifyDamageClientRpc(ulong targetId, float dmg)
    {
        Debug.Log($"Damage applied to {targetId}: {dmg}");
    }

    // �stemci taraf� tahmin
    private void PredictMovement()
    {
        if (clientPredictedIndex < 1) clientPredictedIndex = 1;

        float remaining = arrowSpeed * Time.fixedDeltaTime;

        while (remaining > 0f && clientPredictedIndex < trajectoryPoints.Length)
        {
            Vector3 target = trajectoryPoints[clientPredictedIndex];
            Vector3 toTarget = target - clientPredictedPosition;
            float dist = toTarget.magnitude;

            if (dist < 1e-5f)
            {
                clientPredictedIndex++;
                continue;
            }

            float step = Mathf.Min(remaining, dist);
            Vector3 dir = toTarget / dist;

            clientPredictedPosition += dir * step;
            clientPredictedVelocity = dir * arrowSpeed;
            remaining -= step;

            if (step >= dist - 1e-6f)
                clientPredictedIndex++;
        }

        transform.position = clientPredictedPosition;
        if (autoRotate) LookAtDirection(clientPredictedVelocity);

        // Sunucu indeksi bizden �ndeyse atlama yap
        if (networkPointIndex.Value > clientPredictedIndex)
            clientPredictedIndex = networkPointIndex.Value;
    }

    // Sunucu durumuna do�ru yumu�ak d�zeltme
    private void InterpolateToServerState()
    {
        transform.position = Vector3.Lerp(transform.position, networkPosition.Value, interpolationTime);
        clientPredictedVelocity = Vector3.Lerp(clientPredictedVelocity, networkVelocity.Value, interpolationTime);
    }

    private void OnPositionChanged(Vector3 oldPosition, Vector3 newPosition) { }
    private void OnVelocityChanged(Vector3 oldVelocity, Vector3 newVelocity) { }
    private void OnPointIndexChanged(int oldIndex, int newIndex)
    {
        if (newIndex > clientPredictedIndex) clientPredictedIndex = newIndex;
    }

    private void LookAtDirection(Vector3 velocityOrDir)
    {
        Vector3 dir = velocityOrDir;
        if (dir.sqrMagnitude < 0.0001f) return;

        dir.Normalize();
        Quaternion targetRotation = Quaternion.LookRotation(dir, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSmoothing);
    }

    private void OnTrajectoryCompleted()
    {
        isMoving = false;
        enabled = false;
        StartCoroutine(DestroyAfterDelay(delay));
    }

    public bool IsMoving() => isMoving;

    public void StopMoving()
    {
        isMoving = false;
        enabled = false;
        StopAllCoroutines();
        StartCoroutine(DestroyAfterDelay(delay));
    }

    private IEnumerator DestroyAfterDelay(float delaySeconds)
    {
        yield return new WaitForSeconds(delaySeconds);
        if (IsServer)
        {
            if (currentEffect != null)
            {
                currentEffect.Despawn();
                currentEffect = null;
            }
            var netObj = GetComponent<NetworkObject>();
            netObj.Despawn();
        }
    }

    // Deterministik y�r�nge �retimi
    private Vector3[] GenerateTrajectoryPoints(Vector3 start, Vector3 velocity, int points, float timeStep, float gravityMul)
    {
        int count = Mathf.Max(points, 2);
        var result = new Vector3[count];
        Vector3 g = Physics.gravity * gravityMul;

        for (int i = 0; i < count; i++)
        {
            float t = i * timeStep;
            result[i] = start + velocity * t + 0.5f * g * (t * t);
        }
        return result;
    }
}