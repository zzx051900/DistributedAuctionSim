using UnityEngine;

public enum AgentType
{
    Worker,
    Drone,
    Car
}

public class AgentController : MonoBehaviour
{
    [Header("Basic Info")]
    public int agentId;
    public AgentType agentType = AgentType.Worker;
    public float moveSpeed = 3f;

    [Header("Capability")]
    public float capabilityScore = 1f;   // 智能体基础能力值，越高越适合匹配任务
    public float riskTolerance = 1f;     // 风险承受能力，越高越能接受高风险任务
    public int maxLoad = 2;              // 最大并发/承载上限

    [Header("Load State")]
    public int currentLoad = 0;

    [Header("Runtime State")]
    public int completedTaskCount = 0;
    public bool isFailed = false;

    [Header("Network State")]
    public bool isDisconnected = false;
    public float disconnectEndTime = -1f;

    [Header("Auction Role State")]
    public bool isProposedWinner = false;   // 是否是某任务的临时赢家
    public bool hasConfirmedTask = false;   // 是否已正式确认获得任务

    [Header("Soft Match Settings")]
    public bool useSoftTypeConstraint = true;
    public float typeMismatchPenalty = 10f;

    private TaskPoint currentTask;
    private bool isMoving = false;
    private GameManager gm;

    // 记录初始位置与朝向，供重置使用
    private Vector3 initialPosition;
    private Quaternion initialRotation;

    // 外观控制
    private Renderer agentRenderer;
    private Color initialColor = Color.white;

    void Start()
    {
        gm = FindObjectOfType<GameManager>();

        initialPosition = transform.position;
        initialRotation = transform.rotation;

        agentRenderer = GetComponentInChildren<Renderer>();
        if (agentRenderer != null && agentRenderer.material != null)
        {
            initialColor = agentRenderer.material.color;
        }
    }

    public bool IsBusy()
    {
        return currentTask != null;
    }

    public bool CanBid()
    {
        return !isFailed && !isDisconnected && !IsBusy() && currentLoad < maxLoad;
    }

    public bool CanBid(TaskPoint task)
    {
        if (task == null) return false;
        if (!CanBid()) return false;
        if (task.isCompleted) return false;

        // 软约束：不因类型不匹配而直接禁止投标
        // 若后续想切回硬约束，可将 useSoftTypeConstraint 设为 false
        if (!useSoftTypeConstraint)
        {
            if (task.requiredAgentType != agentType)
            {
                return false;
            }
        }

        return true;
    }

    public float CalculateBid(TaskPoint task)
    {
        if (task == null) return float.MaxValue;
        if (!CanBid(task)) return float.MaxValue;

        float distance = Vector3.Distance(transform.position, task.transform.position);

        // 1. 距离代价
        float distanceCost = distance;

        // 2. 当前负载代价
        float loadCost = 2f * currentLoad;

        // 3. 风险代价：风险越高、容忍度越低，则代价越大
        float safeRiskTolerance = Mathf.Max(0.1f, riskTolerance);
        float riskCost = task.riskLevel / safeRiskTolerance;

        // 4. 紧急任务的类型偏好
        float urgencyAdjustment = 0f;
        if (task.isUrgent)
        {
            switch (agentType)
            {
                case AgentType.Worker:
                    urgencyAdjustment = 0.8f;
                    break;
                case AgentType.Drone:
                    urgencyAdjustment = -2.2f;
                    break;
                case AgentType.Car:
                    urgencyAdjustment = -1.0f;
                    break;
            }
        }

        // 5. 类型匹配收益 / 不匹配惩罚
        bool isTypeMatched = task.requiredAgentType == agentType;
        float typeMatchBonus = isTypeMatched ? 2.5f : 0f;
        float mismatchPenalty = (!isTypeMatched && useSoftTypeConstraint) ? typeMismatchPenalty : 0f;

        // 6. 能力收益
        float capabilityBonus = capabilityScore;

        // 7. 不同智能体的轻量异构偏好
        float heterogeneousAdjustment = 0f;

        switch (agentType)
        {
            case AgentType.Worker:
                // 工人更适合近距离、常规风险任务
                if (distance <= 3f)
                {
                    heterogeneousAdjustment -= 1.0f;
                }
                if (task.riskLevel >= 4f)
                {
                    heterogeneousAdjustment += 1.2f;
                }
                break;

            case AgentType.Drone:
                // 无人机更适合远距离、紧急、高风险任务
                if (distance >= 3f)
                {
                    heterogeneousAdjustment -= 1.2f;
                }
                if (task.riskLevel >= 3.5f)
                {
                    heterogeneousAdjustment -= 1.0f;
                }
                if (task.isUrgent)
                {
                    heterogeneousAdjustment -= 0.8f;
                }
                break;

            case AgentType.Car:
                // 车辆更适合中远距离、较稳定通行任务
                if (distance >= 2.5f)
                {
                    heterogeneousAdjustment -= 0.8f;
                }
                if (task.riskLevel >= 4.5f)
                {
                    heterogeneousAdjustment += 0.8f;
                }
                break;
        }

        float bid =
            distanceCost +
            loadCost +
            riskCost +
            urgencyAdjustment -
            typeMatchBonus -
            capabilityBonus +
            heterogeneousAdjustment +
            mismatchPenalty;

        return bid;
    }

    /// <summary>
    /// 仅表示该智能体在评标阶段被选为“临时赢家”
    /// 这一步不正式接任务，不开始移动
    /// </summary>
    public void SetAsProposedWinner(TaskPoint task)
    {
        if (task == null || isFailed || isDisconnected) return;
        if (task.isCompleted) return;

        isProposedWinner = true;

        Debug.Log($"Agent {agentId} ({agentType}) is proposed winner for Task {task.taskId}");
    }

    /// <summary>
    /// 共识成功后，正式接收任务
    /// </summary>
    public void AssignTask(TaskPoint task)
    {
        if (task == null || isFailed || isDisconnected) return;
        if (task.isCompleted) return;
        if (!task.HasConfirmedWinner()) return;
        if (task.confirmedWinnerId != agentId) return;

        currentTask = task;
        isMoving = false; // 先正式接收，再由 StartExecution 启动执行
        isProposedWinner = false;
        hasConfirmedTask = true;

        currentLoad = Mathf.Clamp(currentLoad + 1, 0, maxLoad);

        Debug.Log($"Agent {agentId} ({agentType}) confirmed assignment for Task {task.taskId}");
    }

    /// <summary>
    /// 正式开始执行任务
    /// </summary>
    public void StartExecution()
    {
        if (isFailed || isDisconnected) return;
        if (currentTask == null) return;
        if (!hasConfirmedTask) return;

        isMoving = true;

        currentTask.StartExecution();
        Debug.Log($"Agent {agentId} ({agentType}) started executing Task {currentTask.taskId}");
    }

    void Update()
    {
        HandleDisconnectionRecovery();

        if (isFailed) return;
        if (isDisconnected) return;
        if (!isMoving || currentTask == null) return;

        Vector3 targetPos = currentTask.transform.position;
        Vector3 dir = targetPos - transform.position;

        if (dir != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRot,
                5f * Time.deltaTime
            );
        }

        transform.position = Vector3.MoveTowards(
            transform.position,
            targetPos,
            moveSpeed * Time.deltaTime
        );

        float dist = Vector3.Distance(transform.position, targetPos);
        if (dist < 0.2f)
        {
            currentTask.MarkCompleted();
            completedTaskCount += 1;

            Debug.Log($"Agent {agentId} ({agentType}) completed Task {currentTask.taskId}");

            if (gm != null)
            {
                gm.OnTaskCompleted(this, currentTask);
            }

            ClearTaskRuntimeState();

            if (gm != null)
            {
                gm.TryAssignRemainingTasks();
            }
        }
    }

    void HandleDisconnectionRecovery()
    {
        if (!isDisconnected) return;
        if (disconnectEndTime < 0f) return;

        if (Time.time >= disconnectEndTime)
        {
            RecoverConnection();
        }
    }

    public void DisconnectTemporarily(float duration)
    {
        if (isFailed) return;
        if (isDisconnected) return;

        isDisconnected = true;
        disconnectEndTime = Time.time + duration;
        isMoving = false;

        Debug.Log($"Agent {agentId} ({agentType}) DISCONNECTED for {duration:F1}s.");

        if (agentRenderer != null && agentRenderer.material != null)
        {
            agentRenderer.material.color = Color.yellow;
        }

        // 如果当前已持有任务但处于执行前/执行中，可根据你的实验需求决定是否释放
        // 第一版这里先不自动释放任务，交给上层共识超时或重拍逻辑处理
    }

    public void RecoverConnection()
    {
        if (!isDisconnected) return;

        isDisconnected = false;
        disconnectEndTime = -1f;

        Debug.Log($"Agent {agentId} ({agentType}) RECONNECTED.");

        if (agentRenderer != null && agentRenderer.material != null)
        {
            agentRenderer.material.color = initialColor;
        }

        if (!isFailed && gm != null)
        {
            gm.TryAssignRemainingTasks();
        }
    }

    public void FailAgent()
    {
        if (isFailed) return;

        isFailed = true;
        isDisconnected = false;
        disconnectEndTime = -1f;
        isMoving = false;

        Debug.Log($"Agent {agentId} ({agentType}) FAILED.");

        if (agentRenderer != null && agentRenderer.material != null)
        {
            agentRenderer.material.color = Color.gray;
        }

        ReleaseCurrentTask();

        if (gm != null)
        {
            gm.TryAssignRemainingTasks();
        }
    }

    private void ReleaseCurrentTask()
    {
        if (currentTask != null && !currentTask.isCompleted)
        {
            Debug.Log($"Agent {agentId} ({agentType}) released Task {currentTask.taskId}");

            currentTask.EnterReauction();
            currentTask = null;
        }

        currentLoad = 0;
        isMoving = false;
        isProposedWinner = false;
        hasConfirmedTask = false;
    }

    private void ClearTaskRuntimeState()
    {
        currentTask = null;
        isMoving = false;
        isProposedWinner = false;
        hasConfirmedTask = false;
        currentLoad = Mathf.Max(0, currentLoad - 1);
    }

    public void ResetAgentState()
    {
        currentLoad = 0;
        completedTaskCount = 0;
        isFailed = false;
        isDisconnected = false;
        disconnectEndTime = -1f;

        currentTask = null;
        isMoving = false;
        isProposedWinner = false;
        hasConfirmedTask = false;

        transform.position = initialPosition;
        transform.rotation = initialRotation;

        if (agentRenderer != null && agentRenderer.material != null)
        {
            agentRenderer.material.color = initialColor;
        }

        Debug.Log($"Agent {agentId} ({agentType}) reset to initial state.");
    }
}