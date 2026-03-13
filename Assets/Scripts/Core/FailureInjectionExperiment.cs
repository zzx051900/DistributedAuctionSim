using System.Collections.Generic;
using UnityEngine;

public class FailureInjectionExperiment : MonoBehaviour
{
    [Header("References")]
    public GameManager gameManager;

    [Header("Injection Switch")]
    public bool enableFailureInjection = true;

    [Header("Injection Strategy")]
    public bool injectOnlyOncePerRun = true;     // 每轮实验只注入一次
    public int targetTaskId = -1;                // -1 表示对任意任务生效；否则只盯指定 taskId
    public float injectDelay = 0.15f;            // 进入 Confirming 后延迟多久触发失效
    public bool requireProposedWinner = true;    // 必须已有 proposedWinner 才触发

    [Header("Optional Filters")]
    public bool onlyWhenTaskNotReauctioned = false;   // 只在首次拍卖时注入
    public bool onlyIfWinnerNotExecuting = true;      // 只在还未进入执行时注入

    private bool hasInjectedThisRun = false;

    // 记录已安排过注入的 task，避免反复安排
    private HashSet<int> scheduledTaskIds = new HashSet<int>();

    void Start()
    {
        if (gameManager == null)
        {
            gameManager = FindObjectOfType<GameManager>();
        }
    }

    void Update()
    {
        if (!enableFailureInjection) return;
        if (gameManager == null) return;
        if (injectOnlyOncePerRun && hasInjectedThisRun) return;

        TaskPoint[] allTasks = FindObjectsOfType<TaskPoint>();
        if (allTasks == null || allTasks.Length == 0) return;

        foreach (TaskPoint task in allTasks)
        {
            if (task == null) continue;

            if (!ShouldInjectForTask(task))
                continue;

            if (scheduledTaskIds.Contains(task.taskId))
                continue;

            scheduledTaskIds.Add(task.taskId);
            StartCoroutine(InjectFailureAfterDelay(task, injectDelay));
        }
    }

    bool ShouldInjectForTask(TaskPoint task)
    {
        if (task == null) return false;

        // 只针对指定 taskId
        if (targetTaskId >= 0 && task.taskId != targetTaskId)
            return false;

        // 必须在 Confirming 阶段
        if (task.currentState != TaskPoint.TaskState.Confirming)
            return false;

        // 可选：只在首次拍卖时触发
        if (onlyWhenTaskNotReauctioned && task.hasBeenReassigned)
            return false;

        // 必须已经有候选赢家
        if (requireProposedWinner && task.proposedWinnerId < 0)
            return false;

        return true;
    }

    System.Collections.IEnumerator InjectFailureAfterDelay(TaskPoint task, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (task == null) yield break;
        if (gameManager == null) yield break;

        // 二次确认，防止延迟期间状态已变化
        if (task.currentState != TaskPoint.TaskState.Confirming)
            yield break;

        int winnerId = task.proposedWinnerId;
        if (winnerId < 0 || winnerId >= gameManager.agents.Length)
            yield break;

        AgentController winner = gameManager.agents[winnerId];
        if (winner == null) yield break;
        if (winner.isFailed) yield break;

        if (onlyIfWinnerNotExecuting && winner.hasConfirmedTask && !winner.IsBusy())
        {
            // 这个条件通常不会卡住，但保留扩展空间
        }

        Debug.Log(
            $"[FailureInjection] Inject failure on proposed winner Agent {winner.agentId} " +
            $"for Task {task.taskId} during CONFIRMING."
        );

        winner.FailAgent();

        hasInjectedThisRun = true;
    }

    public void ResetInjectionState()
    {
        hasInjectedThisRun = false;
        scheduledTaskIds.Clear();
    }
}