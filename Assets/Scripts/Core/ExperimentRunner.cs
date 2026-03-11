using UnityEngine;

public class ExperimentRunner : MonoBehaviour
{
    [Header("Experiment Settings")]
    public GameManager gameManager;

    public bool autoRunOnStart = true;
    public int totalRuns = 10;
    public float delayBetweenRuns = 2f;

    [Header("Runtime Info")]
    public int currentRun = 0;
    public bool isBatchRunning = false;

    private float nextRunTimer = 0f;
    private bool waitingNextRun = false;

    void Start()
    {
        if (autoRunOnStart)
        {
            StartBatchExperiment();
        }
    }

    void Update()
    {
        if (waitingNextRun)
        {
            nextRunTimer -= Time.deltaTime;
            if (nextRunTimer <= 0f)
            {
                waitingNextRun = false;
                StartNextRun();
            }
        }
    }

    public void StartBatchExperiment()
    {
        if (gameManager == null)
        {
            Debug.LogError("ExperimentRunner: GameManager is not assigned.");
            return;
        }

        currentRun = 0;
        isBatchRunning = true;
        waitingNextRun = false;

        Debug.Log("===== Batch Experiment Started =====");
        StartNextRun();
    }

    public void OnSingleRunFinished()
    {
        if (!isBatchRunning) return;

        Debug.Log($"===== Run {currentRun}/{totalRuns} Finished =====");

        if (currentRun >= totalRuns)
        {
            isBatchRunning = false;
            Debug.Log("===== All Batch Experiments Finished =====");
            return;
        }

        waitingNextRun = true;
        nextRunTimer = delayBetweenRuns;
    }

    private void StartNextRun()
    {
        if (!isBatchRunning) return;

        if (currentRun >= totalRuns)
        {
            isBatchRunning = false;
            Debug.Log("===== All Batch Experiments Finished =====");
            return;
        }

        currentRun++;
        Debug.Log($"===== Starting Run {currentRun}/{totalRuns} =====");

        gameManager.StartSingleExperiment(currentRun);
    }
}