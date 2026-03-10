using UnityEngine;
using System.Collections.Generic;

public class CloudGenerator : MonoBehaviour
{
    [Header("Cloud Settings")]
    public GameObject cloudPrefab;
    public int cloudCount = 30;

    [Header("Spawn Area")]
    public float areaWidth = 200f;
    public float areaDepth = 200f;
    public float heightMin = 40f;
    public float heightMax = 80f;

    [Header("Wind Settings")]
    public Vector3 windDirection = new Vector3(1, 0, 0);
    public float windSpeed = 5f;

    [Header("Variation Settings")]
    public float minScale = 0.8f;
    public float maxScale = 1.5f;
    public float maxTiltAngle = 5f; // small X/Z tilt

    private List<GameObject> clouds = new List<GameObject>();

    void Start()
    {
        SpawnClouds();
    }

    void Update()
    {
        MoveClouds();
    }

    void SpawnClouds()
    {
        for (int i = 0; i < cloudCount; i++)
        {
            Vector3 randomPos = new Vector3(
                Random.Range(-areaWidth / 2f, areaWidth / 2f),
                Random.Range(heightMin, heightMax),
                Random.Range(-areaDepth / 2f, areaDepth / 2f)
            );

            // Random rotation
            float randomY = Random.Range(0f, 360f);
            float randomX = Random.Range(-maxTiltAngle, maxTiltAngle);
            float randomZ = Random.Range(-maxTiltAngle, maxTiltAngle);

            Quaternion randomRotation = Quaternion.Euler(randomX, randomY, randomZ);

            GameObject cloud = Instantiate(
                cloudPrefab,
                transform.position + randomPos,
                randomRotation,
                transform
            );

            // Random scale
            float scale = Random.Range(minScale, maxScale);
            cloud.transform.localScale = Vector3.one * scale;

            clouds.Add(cloud);
        }
    }

    void MoveClouds()
    {
        Vector3 movement = windDirection.normalized * windSpeed * Time.deltaTime;

        foreach (GameObject cloud in clouds)
        {
            cloud.transform.position += movement;

            Vector3 localPos = cloud.transform.position - transform.position;

            // Wrap inside area
            if (localPos.x > areaWidth / 2f)
                localPos.x = -areaWidth / 2f;
            else if (localPos.x < -areaWidth / 2f)
                localPos.x = areaWidth / 2f;

            if (localPos.z > areaDepth / 2f)
                localPos.z = -areaDepth / 2f;
            else if (localPos.z < -areaDepth / 2f)
                localPos.z = areaDepth / 2f;

            cloud.transform.position = transform.position + localPos;
        }
    }
}