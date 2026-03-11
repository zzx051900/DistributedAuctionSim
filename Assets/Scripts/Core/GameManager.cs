using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("Agents")]
    public AgentController[] agents;

    [Header("Task Settings")]
    public GameObject taskPrefab;
    public int taskCount = 3;

    public float minX = -4f;
    public float maxX = 4f;
    public float minZ = 2f;
    public float maxZ = 6f;

    [Header("Heterogeneous Task Settings")]
    public bool enableRandomTaskType = true;
    public bool enableRandomUrgency = true;
    public bool enableRandomRisk = true;
    public float minRiskLevel = 1f;
    public float maxRiskLevel = 5f;

    [Header("Consensus Settings")]
    public int requiredConfirmations = 1;
    public float consensusTimeout = 1.5f;
    public float reauctionDelay = 0.5f;

    [Header("Weak Network Simulation")]
    public bool enableNetworkDelay = true;
    public float minNetworkDelay = 0.2f;
    public float maxNetworkDelay = 1.0f;

    public bool enablePacketLoss = false;
    [Range(0f, 1f)]
    public float packetLossRate = 0.1f;

    public bool enableAgentDisconnection = false;
    public float disconnectionDuration = 3f;

    [Header("Experiment Runner")]
    public ExperimentRunner experimentRunner;
    public int currentRunId = 0;

    private List<TaskPoint> tasks = new List<TaskPoint>();

    [Header("Auction UI Data")]
    public int currentAuctionTaskId = -1;
    public int currentWinnerAgentId = -1;
    public string auctionInfo = "";

    public List<string> auctionHistory = new List<string>();
    public int maxAuctionHistory = 6;

    public Dictionary<int, float> latestBids = new Dictionary<int, float>();

    [Header("Statistics")]
    public int totalTaskCount = 0;
    public int completedTaskCount = 0;
    public int totalAssignmentCount = 0;
    public int failedAgentCount = 0;

    [Header("Consensus Statistics")]
    public int totalConsensusSuccess = 0;
    public int totalConsensusFail = 0;
    public int totalReauctionCount = 0;

    [Header("Communication Statistics")]
    public int taskAnnouncementCount = 0;
    public int bidMessageCount = 0;
    public int evaluationCount = 0;
    public int confirmMessageCount = 0;
    public int reassignMessageCount = 0;
    public int totalMessageCount = 0;

    [Header("Reassignment Statistics")]
    public int tasksNeedingReassignment = 0;
    public int successfulReassignments = 0;
    public int failedReassignments = 0;

    private float startTime;
    private float endTime;
    private bool allTasksFinished = false;

    private string currentTriggerReason = "NORMAL";

    private bool isAuctionRunning = false;
    private int globalAuctionRound = 0;

    // =========================
    // 延迟消息队列
    // =========================
    private class DelayedAction
    {
        public float executeTime;
        public System.Action action;
        public string description;
    }

    private List<DelayedAction> delayedActions = new List<DelayedAction>();

    // 记录哪些任务已经安排过“确认消息”，避免在 ConsensusPhase 中每帧重复安排
    private HashSet<int> tasksWithScheduledConsensus = new HashSet<int>();

    void Start()
    {
        Debug.Log("GameManager Ready.");

        if (experimentRunner == null)
        {
            Debug.Log("No ExperimentRunner found, start single experiment directly.");
            StartSingleExperiment(1);
        }
    }

    void Update()
    {
        CheckFailInput();
        ProcessDelayedActions();
    }

    // =========================
    // 自动实验入口
    // =========================
    public void StartSingleExperiment(int runId)
    {
        currentRunId = runId;

        Debug.Log($"GameManager: StartSingleExperiment Run {currentRunId}");

        ResetExperimentState();

        if (ExperimentLogger.Instance != null)
        {
            ExperimentLogger.Instance.ResetAuctionRound();
        }

        GenerateTasks();
        totalTaskCount = tasks.Count;

        startTime = Time.time;
        endTime = 0f;
        allTasksFinished = false;

        TryAssignRemainingTasks();
    }

    public void ResetExperimentState()
    {
        TaskPoint[] oldTasks = FindObjectsOfType<TaskPoint>();
        foreach (TaskPoint oldTask in oldTasks)
        {
            if (oldTask != null)
            {
                Destroy(oldTask.gameObject);
            }
        }

        tasks.Clear();
        delayedActions.Clear();
        tasksWithScheduledConsensus.Clear();

        auctionHistory.Clear();
        latestBids.Clear();
        auctionInfo = "";
        currentAuctionTaskId = -1;
        currentWinnerAgentId = -1;

        totalTaskCount = 0;
        completedTaskCount = 0;
        totalAssignmentCount = 0;
        failedAgentCount = 0;

        totalConsensusSuccess = 0;
        totalConsensusFail = 0;
        totalReauctionCount = 0;

        taskAnnouncementCount = 0;
        bidMessageCount = 0;
        evaluationCount = 0;
        confirmMessageCount = 0;
        reassignMessageCount = 0;
        totalMessageCount = 0;

        tasksNeedingReassignment = 0;
        successfulReassignments = 0;
        failedReassignments = 0;

        startTime = 0f;
        endTime = 0f;
        allTasksFinished = false;

        currentTriggerReason = "NORMAL";
        isAuctionRunning = false;
        globalAuctionRound = 0;

        if (agents != null)
        {
            foreach (AgentController agent in agents)
            {
                if (agent != null)
                {
                    agent.ResetAgentState();
                }
            }
        }
    }

    // =========================
    // 任务生成
    // =========================
    void GenerateTasks()
    {
        TaskPoint[] oldTasks = FindObjectsOfType<TaskPoint>();
        foreach (TaskPoint oldTask in oldTasks)
        {
            if (oldTask != null)
            {
                Destroy(oldTask.gameObject);
            }
        }

        tasks.Clear();
        delayedActions.Clear();
        tasksWithScheduledConsensus.Clear();

        auctionHistory.Clear();
        latestBids.Clear();
        auctionInfo = "";
        currentAuctionTaskId = -1;
        currentWinnerAgentId = -1;

        for (int i = 0; i < taskCount; i++)
        {
            float x = Random.Range(minX, maxX);
            float z = Random.Range(minZ, maxZ);
            Vector3 spawnPos = new Vector3(x, 0.5f, z);

            GameObject taskObj = Instantiate(taskPrefab, spawnPos, Quaternion.identity);
            taskObj.name = "Task_" + i;

            TaskPoint taskPoint = taskObj.GetComponent<TaskPoint>();
            if (taskPoint != null)
            {
                taskPoint.taskId = i;
                taskPoint.ResetTask();

                taskPoint.requiredAgentType = GenerateTaskRequiredType();
                taskPoint.riskLevel = GenerateTaskRiskLevel();
                taskPoint.isUrgent = GenerateTaskUrgency();

                tasks.Add(taskPoint);

                taskAnnouncementCount++;
                totalMessageCount++;

                Debug.Log(
                    $"Generate Task {taskPoint.taskId} | " +
                    $"Type={taskPoint.requiredAgentType} | " +
                    $"Risk={taskPoint.riskLevel:F1} | " +
                    $"Urgent={taskPoint.isUrgent}"
                );
            }
        }
    }

    AgentType GenerateTaskRequiredType()
    {
        if (!enableRandomTaskType)
            return AgentType.Worker;

        int value = Random.Range(0, 3);
        switch (value)
        {
            case 0: return AgentType.Worker;
            case 1: return AgentType.Drone;
            case 2: return AgentType.Car;
            default: return AgentType.Worker;
        }
    }

    float GenerateTaskRiskLevel()
    {
        if (!enableRandomRisk)
            return 1f;

        return Random.Range(minRiskLevel, maxRiskLevel);
    }

    bool GenerateTaskUrgency()
    {
        if (!enableRandomUrgency)
            return false;

        return Random.value > 0.5f;
    }

    // =========================
    // 弱网辅助
    // =========================
    float GetRandomDelay()
    {
        if (!enableNetworkDelay)
            return 0f;

        if (maxNetworkDelay < minNetworkDelay)
            maxNetworkDelay = minNetworkDelay;

        return Random.Range(minNetworkDelay, maxNetworkDelay);
    }

    bool ShouldDropPacket()
    {
        if (!enablePacketLoss)
            return false;

        return Random.value < packetLossRate;
    }

    void ScheduleAction(System.Action action, string description)
    {
        if (action == null) return;

        if (ShouldDropPacket())
        {
            Debug.Log($"[WeakNetwork] Packet dropped: {description}");
            return;
        }

        float delay = GetRandomDelay();

        delayedActions.Add(new DelayedAction
        {
            executeTime = Time.time + delay,
            action = action,
            description = description
        });

        Debug.Log($"[WeakNetwork] Scheduled: {description}, delay={delay:F2}s");
    }

    void ProcessDelayedActions()
    {
        for (int i = delayedActions.Count - 1; i >= 0; i--)
        {
            if (Time.time >= delayedActions[i].executeTime)
            {
                DelayedAction delayed = delayedActions[i];
                delayedActions.RemoveAt(i);

                try
                {
                    delayed.action?.Invoke();
                    Debug.Log($"[WeakNetwork] Executed: {delayed.description}");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[WeakNetwork] Error executing delayed action: {delayed.description}\n{ex}");
                }
            }
        }
    }

    void ScheduleConsensusConfirmations(
        TaskPoint task,
        AgentController proposedAgent,
        float bestBid,
        string agent0State, string agent0Bid,
        string agent1State, string agent1Bid,
        string agent2State, string agent2Bid)
    {
        if (task == null || proposedAgent == null) return;
        if (tasksWithScheduledConsensus.Contains(task.taskId)) return;

        tasksWithScheduledConsensus.Add(task.taskId);

        int confirmNeed = Mathf.Max(1, task.requiredConfirmations);

        for (int i = 0; i < confirmNeed; i++)
        {
            int confirmerId = 1000 + i;

            ScheduleAction(() =>
            {
                if (task == null) return;
                if (task.isCompleted) return;
                if (!task.IsConsensusPending()) return;
                if (proposedAgent == null) return;
                if (proposedAgent.isFailed) return;
                if (proposedAgent.isDisconnected) return;

                bool confirmed = task.AddConfirmationFromAgent(confirmerId);

                if (confirmed)
                {
                    confirmMessageCount++;
                    totalMessageCount++;

                    auctionInfo =
                        $"[Round {task.auctionRound}] " +
                        BuildTaskHeader(task) +
                        $"Stage: CONFIRMING\n" +
                        $"Proposed Winner: Agent {task.proposedWinnerId}\n" +
                        $"Bid: {bestBid:F2}\n" +
                        $"Confirm Count: {task.confirmCount}/{task.requiredConfirmations}\n" +
                        $"Consensus Result: {task.GetConsensusResultText()}";

                    SaveAuctionHistory(auctionInfo);
                    Debug.Log(auctionInfo);

                    LogAuctionStage(
                        task,
                        "CONFIRMING",
                        currentTriggerReason,
                        agent0State, agent0Bid,
                        agent1State, agent1Bid,
                        agent2State, agent2Bid,
                        task.proposedWinnerId,
                        -1,
                        bestBid.ToString("F2"),
                        task.confirmCount,
                        task.requiredConfirmations,
                        task.GetConsensusResultText(),
                        false
                    );
                }
            }, $"ConsensusConfirm Task {task.taskId} #{i + 1}");
        }
    }

    void ClearConsensusScheduleForTask(TaskPoint task)
    {
        if (task == null) return;
        tasksWithScheduledConsensus.Remove(task.taskId);
    }

    // =========================
    // 日志辅助
    // =========================
    void SaveAuctionHistory(string info)
    {
        if (string.IsNullOrEmpty(info)) return;

        auctionHistory.Add(info);

        if (auctionHistory.Count > maxAuctionHistory)
        {
            auctionHistory.RemoveAt(0);
        }
    }

    void LogAuctionStage(
        TaskPoint task,
        string stage,
        string triggerReason,
        string agent0State, string agent0Bid,
        string agent1State, string agent1Bid,
        string agent2State, string agent2Bid,
        int proposedWinnerId,
        int confirmedWinnerId,
        string winnerBid,
        int confirmCount,
        int requiredConfirms,
        string consensusResult,
        bool isReauction)
    {
        if (ExperimentLogger.Instance == null || task == null) return;

        ExperimentLogger.Instance.LogAuctionDetailExtended(
            Time.time,
            task.taskId,
            task.requiredAgentType.ToString(),
            task.riskLevel,
            task.isUrgent,
            triggerReason,
            stage,
            agent0State, agent0Bid,
            agent1State, agent1Bid,
            agent2State, agent2Bid,
            proposedWinnerId,
            confirmedWinnerId,
            winnerBid,
            confirmCount,
            requiredConfirms,
            consensusResult,
            isReauction,
            task.hasBeenReassigned ? 1 : 0,
            task.reassignCount,
            task.auctionRound
        );
    }

    string BuildTaskHeader(TaskPoint task)
    {
        if (task == null) return "";

        return
            $"Task {task.taskId}\n" +
            $"Type: {task.requiredAgentType}\n" +
            $"Risk: {task.riskLevel:F1}\n" +
            $"Urgent: {(task.isUrgent ? "YES" : "NO")}\n";
    }

    string GetAgentStateForLog(int index)
    {
        if (agents == null || index < 0 || index >= agents.Length) return "";
        AgentController agent = agents[index];
        if (agent == null) return "";

        if (agent.isFailed) return "FAILED";
        if (agent.isDisconnected) return "DISCONNECTED";
        if (agent.hasConfirmedTask) return "EXECUTING";
        if (agent.isProposedWinner) return "PROPOSED";
        if (agent.IsBusy()) return "BUSY";
        return "IDLE";
    }

    // =========================
    // 拍卖逻辑
    // =========================
    public void TryAssignRemainingTasks()
    {
        if (!isAuctionRunning)
        {
            StartCoroutine(AssignTasksByAuctionCoroutine());
        }
    }

    IEnumerator AssignTasksByAuctionCoroutine()
    {
        isAuctionRunning = true;

        if (agents == null || agents.Length == 0)
        {
            isAuctionRunning = false;
            yield break;
        }

        foreach (TaskPoint task in tasks)
        {
            if (task == null) continue;
            if (task.isCompleted) continue;
            if (task.currentState == TaskPoint.TaskState.Executing) continue;
            if (task.currentState == TaskPoint.TaskState.Committed) continue;
            if (task.currentState == TaskPoint.TaskState.Confirming) continue;

            yield return StartCoroutine(RunAuctionForTask(task));
        }

        isAuctionRunning = false;
        CheckAllTasksCompleted();
    }

    IEnumerator RunAuctionForTask(TaskPoint task)
    {
        if (task == null || task.isCompleted)
            yield break;

        globalAuctionRound++;
        task.StartAuctionRound(globalAuctionRound);

        ClearConsensusScheduleForTask(task);

        currentAuctionTaskId = task.taskId;
        currentWinnerAgentId = -1;
        latestBids.Clear();

        AgentController bestAgent = null;
        float bestBid = float.MaxValue;

        string agent0State = "", agent0Bid = "";
        string agent1State = "", agent1Bid = "";
        string agent2State = "", agent2Bid = "";

        auctionInfo =
            $"[Round {task.auctionRound}] " +
            BuildTaskHeader(task) +
            $"Trigger: {currentTriggerReason}\n" +
            $"Stage: BIDDING\n";

        for (int i = 0; i < agents.Length; i++)
        {
            AgentController agent = agents[i];
            if (agent == null) continue;

            string state = "";
            string bidText = "";

            if (agent.isFailed)
            {
                state = "FAILED";
                auctionInfo += $"Agent {agent.agentId} ({agent.agentType}): FAILED\n";
            }
            else if (agent.isDisconnected)
            {
                state = "DISCONNECTED";
                auctionInfo += $"Agent {agent.agentId} ({agent.agentType}): DISCONNECTED\n";
            }
            else if (!agent.CanBid(task))
            {
                state = "UNAVAILABLE";
                auctionInfo += $"Agent {agent.agentId} ({agent.agentType}): UNAVAILABLE\n";
            }
            else
            {
                state = "BIDDER";
                float bid = agent.CalculateBid(task);
                bidText = bid.ToString("F2");
                latestBids[agent.agentId] = bid;

                bidMessageCount++;
                totalMessageCount++;

                auctionInfo += $"Agent {agent.agentId} ({agent.agentType}): bid = {bid:F2}\n";

                if (bid < bestBid)
                {
                    bestBid = bid;
                    bestAgent = agent;
                }
            }

            if (i == 0) { agent0State = state; agent0Bid = bidText; }
            if (i == 1) { agent1State = state; agent1Bid = bidText; }
            if (i == 2) { agent2State = state; agent2Bid = bidText; }
        }

        SaveAuctionHistory(auctionInfo);
        Debug.Log(auctionInfo);

        LogAuctionStage(
            task,
            "BIDDING",
            currentTriggerReason,
            agent0State, agent0Bid,
            agent1State, agent1Bid,
            agent2State, agent2Bid,
            -1,
            -1,
            "",
            0,
            requiredConfirmations,
            "PENDING",
            false
        );

        task.EnterEvaluating();
        evaluationCount++;
        totalMessageCount++;

        auctionInfo =
            $"[Round {task.auctionRound}] " +
            BuildTaskHeader(task) +
            $"Trigger: {currentTriggerReason}\n" +
            $"Stage: EVALUATING\n";

        int winnerAgentId = -1;
        string winnerBidString = "";

        if (bestAgent == null)
        {
            auctionInfo += "Winner: None\n";
            SaveAuctionHistory(auctionInfo);
            Debug.Log(auctionInfo);

            LogAuctionStage(
                task,
                "EVALUATING",
                currentTriggerReason,
                agent0State, agent0Bid,
                agent1State, agent1Bid,
                agent2State, agent2Bid,
                -1,
                -1,
                "",
                0,
                requiredConfirmations,
                "NO_CANDIDATE",
                false
            );

            if (task.hasBeenReassigned)
            {
                failedReassignments++;
            }

            currentTriggerReason = "NORMAL";
            yield break;
        }

        winnerAgentId = bestAgent.agentId;
        winnerBidString = bestBid.ToString("F2");

        auctionInfo += $"Proposed Winner: Agent {winnerAgentId} ({bestAgent.agentType}), bid = {bestBid:F2}\n";
        SaveAuctionHistory(auctionInfo);
        Debug.Log(auctionInfo);

        LogAuctionStage(
            task,
            "EVALUATING",
            currentTriggerReason,
            agent0State, agent0Bid,
            agent1State, agent1Bid,
            agent2State, agent2Bid,
            winnerAgentId,
            -1,
            winnerBidString,
            0,
            requiredConfirmations,
            "PROPOSED",
            false
        );

        currentWinnerAgentId = winnerAgentId;

        task.SetProposedWinner(winnerAgentId, requiredConfirmations, consensusTimeout);
        bestAgent.SetAsProposedWinner(task);

        confirmMessageCount++;
        totalMessageCount++;

        auctionInfo =
            $"[Round {task.auctionRound}] " +
            BuildTaskHeader(task) +
            $"Trigger: {currentTriggerReason}\n" +
            $"Stage: CONFIRMING\n" +
            $"Proposed Winner: Agent {winnerAgentId} ({bestAgent.agentType}), bid = {bestBid:F2}\n" +
            $"Confirm Count: {task.confirmCount}/{task.requiredConfirmations}\n" +
            $"Consensus Result: {task.GetConsensusResultText()}";

        SaveAuctionHistory(auctionInfo);
        Debug.Log(auctionInfo);

        LogAuctionStage(
            task,
            "CONFIRMING",
            currentTriggerReason,
            agent0State, agent0Bid,
            agent1State, agent1Bid,
            agent2State, agent2Bid,
            winnerAgentId,
            -1,
            winnerBidString,
            task.confirmCount,
            task.requiredConfirmations,
            task.GetConsensusResultText(),
            false
        );

        yield return StartCoroutine(ConsensusPhase(
            task,
            bestAgent,
            bestBid,
            agent0State, agent0Bid,
            agent1State, agent1Bid,
            agent2State, agent2Bid
        ));

        currentTriggerReason = "NORMAL";
    }

    IEnumerator ConsensusPhase(
        TaskPoint task,
        AgentController proposedAgent,
        float bestBid,
        string agent0State, string agent0Bid,
        string agent1State, string agent1Bid,
        string agent2State, string agent2Bid)
    {
        if (task == null || proposedAgent == null)
            yield break;

        ScheduleConsensusConfirmations(
            task,
            proposedAgent,
            bestBid,
            agent0State, agent0Bid,
            agent1State, agent1Bid,
            agent2State, agent2Bid
        );

        while (true)
        {
            if (task == null)
            {
                ClearConsensusScheduleForTask(task);
                yield break;
            }

            if (task.isCompleted)
            {
                ClearConsensusScheduleForTask(task);
                yield break;
            }

            // 1. 候选赢家彻底失效
            if (proposedAgent.isFailed)
            {
                task.MarkConsensusFailedByAgentFailure();
                ClearConsensusScheduleForTask(task);

                totalConsensusFail++;
                totalReauctionCount++;
                tasksNeedingReassignment++;
                reassignMessageCount++;
                totalMessageCount++;

                auctionInfo =
                    $"[Round {task.auctionRound}] " +
                    BuildTaskHeader(task) +
                    $"Stage: CONSENSUS_FAILED\n" +
                    $"Reason: Proposed winner failed\n" +
                    $"Consensus Result: {task.GetConsensusResultText()}\n" +
                    $"Action: Reauction";

                SaveAuctionHistory(auctionInfo);
                Debug.Log(auctionInfo);

                LogAuctionStage(
                    task,
                    "CONSENSUS_FAILED",
                    currentTriggerReason,
                    agent0State, agent0Bid,
                    agent1State, agent1Bid,
                    agent2State, agent2Bid,
                    task.proposedWinnerId,
                    -1,
                    bestBid.ToString("F2"),
                    task.confirmCount,
                    task.requiredConfirmations,
                    task.GetConsensusResultText(),
                    true
                );

                task.EnterReauction();

                yield return new WaitForSeconds(reauctionDelay);
                isAuctionRunning = false;
                TryAssignRemainingTasks();
                yield break;
            }

            // 2. 候选赢家暂时断连：不立即判失败，等待超时或恢复
            if (proposedAgent.isDisconnected)
            {
                auctionInfo =
                    $"[Round {task.auctionRound}] " +
                    BuildTaskHeader(task) +
                    $"Stage: CONFIRMING\n" +
                    $"Proposed Winner: Agent {task.proposedWinnerId}\n" +
                    $"Bid: {bestBid:F2}\n" +
                    $"Network State: DISCONNECTED\n" +
                    $"Confirm Count: {task.confirmCount}/{task.requiredConfirmations}\n" +
                    $"Consensus Result: WAITING";

                Debug.Log(auctionInfo);
            }

            // 3. 共识成功 -> 正式提交
            if (task.consensusSucceeded && task.HasConfirmedWinner())
            {
                ClearConsensusScheduleForTask(task);

                totalConsensusSuccess++;
                totalAssignmentCount++;

                currentWinnerAgentId = task.confirmedWinnerId;

                proposedAgent.AssignTask(task);

                auctionInfo =
                    $"[Round {task.auctionRound}] " +
                    BuildTaskHeader(task) +
                    $"Stage: COMMITTED\n" +
                    $"Confirmed Winner: Agent {task.confirmedWinnerId} ({proposedAgent.agentType})\n" +
                    $"Bid: {bestBid:F2}\n" +
                    $"Confirm Count: {task.confirmCount}/{task.requiredConfirmations}\n" +
                    $"Consensus Result: {task.GetConsensusResultText()}";

                SaveAuctionHistory(auctionInfo);
                Debug.Log(auctionInfo);

                LogAuctionStage(
                    task,
                    "COMMITTED",
                    currentTriggerReason,
                    agent0State, agent0Bid,
                    agent1State, agent1Bid,
                    agent2State, agent2Bid,
                    task.proposedWinnerId,
                    task.confirmedWinnerId,
                    bestBid.ToString("F2"),
                    task.confirmCount,
                    task.requiredConfirmations,
                    task.GetConsensusResultText(),
                    false
                );

                proposedAgent.StartExecution();

                auctionInfo =
                    $"[Round {task.auctionRound}] " +
                    BuildTaskHeader(task) +
                    $"Stage: EXECUTING\n" +
                    $"Executor: Agent {task.confirmedWinnerId} ({proposedAgent.agentType})\n" +
                    $"Consensus Result: RUNNING";

                SaveAuctionHistory(auctionInfo);
                Debug.Log(auctionInfo);

                LogAuctionStage(
                    task,
                    "EXECUTING",
                    currentTriggerReason,
                    agent0State, agent0Bid,
                    agent1State, agent1Bid,
                    agent2State, agent2Bid,
                    task.proposedWinnerId,
                    task.confirmedWinnerId,
                    bestBid.ToString("F2"),
                    task.confirmCount,
                    task.requiredConfirmations,
                    "RUNNING",
                    false
                );

                yield break;
            }

            // 4. 共识超时
            if (task.IsConsensusTimeout())
            {
                task.MarkConsensusTimeout();
                ClearConsensusScheduleForTask(task);

                totalConsensusFail++;
                totalReauctionCount++;
                tasksNeedingReassignment++;
                reassignMessageCount++;
                totalMessageCount++;

                string timeoutReason = proposedAgent.isDisconnected
                    ? "Timeout (proposed winner disconnected)"
                    : "Timeout";

                auctionInfo =
                    $"[Round {task.auctionRound}] " +
                    BuildTaskHeader(task) +
                    $"Stage: CONSENSUS_FAILED\n" +
                    $"Reason: {timeoutReason}\n" +
                    $"Confirm Count: {task.confirmCount}/{task.requiredConfirmations}\n" +
                    $"Consensus Result: {task.GetConsensusResultText()}\n" +
                    $"Action: Reauction";

                SaveAuctionHistory(auctionInfo);
                Debug.Log(auctionInfo);

                LogAuctionStage(
                    task,
                    "CONSENSUS_FAILED",
                    currentTriggerReason,
                    agent0State, agent0Bid,
                    agent1State, agent1Bid,
                    agent2State, agent2Bid,
                    task.proposedWinnerId,
                    -1,
                    bestBid.ToString("F2"),
                    task.confirmCount,
                    task.requiredConfirmations,
                    task.GetConsensusResultText(),
                    true
                );

                task.EnterReauction();

                yield return new WaitForSeconds(reauctionDelay);
                isAuctionRunning = false;
                TryAssignRemainingTasks();
                yield break;
            }

            yield return null;
        }
    }

    // =========================
    // 任务完成与实验结束
    // =========================
    public void OnTaskCompleted(AgentController agent, TaskPoint task)
    {
        completedTaskCount++;
        Debug.Log($"Completed: {completedTaskCount}/{totalTaskCount}");

        if (task != null)
        {
            ClearConsensusScheduleForTask(task);

            if (task.hasBeenReassigned)
            {
                successfulReassignments++;
            }

            string finalInfo =
                $"[Round {task.auctionRound}] Task {task.taskId}\n" +
                $"Type: {task.requiredAgentType}\n" +
                $"Risk: {task.riskLevel:F1}\n" +
                $"Urgent: {(task.isUrgent ? "YES" : "NO")}\n" +
                $"Stage: COMPLETED\n" +
                $"Completed By: Agent {(agent != null ? agent.agentId : -1)}" +
                $"{(agent != null ? $" ({agent.agentType})" : "")}\n" +
                $"Consensus Result: {task.GetConsensusResultText()}";

            auctionInfo = finalInfo;
            SaveAuctionHistory(finalInfo);
            Debug.Log(finalInfo);

            LogAuctionStage(
                task,
                "COMPLETED",
                "TASK_FINISHED",
                GetAgentStateForLog(0), "",
                GetAgentStateForLog(1), "",
                GetAgentStateForLog(2), "",
                task.proposedWinnerId,
                task.confirmedWinnerId,
                "",
                task.confirmCount,
                task.requiredConfirmations,
                "DONE",
                false
            );
        }

        CheckAllTasksCompleted();
    }

    void CheckAllTasksCompleted()
    {
        if (allTasksFinished) return;

        if (completedTaskCount >= totalTaskCount && totalTaskCount > 0)
        {
            allTasksFinished = true;
            endTime = Time.time;

            Debug.Log("All tasks completed.");
            Debug.Log($"Run ID: {currentRunId}");
            Debug.Log($"Total completion time: {GetTotalCompletionTime():F2} s");
            Debug.Log($"Completion rate: {GetCompletionRate() * 100f:F2}%");
            Debug.Log($"Fairness index: {GetFairnessIndex():F3}");
            Debug.Log($"Consensus success: {totalConsensusSuccess}");
            Debug.Log($"Consensus fail: {totalConsensusFail}");
            Debug.Log($"Reauction count: {totalReauctionCount}");
            Debug.Log($"Task announcements: {taskAnnouncementCount}");
            Debug.Log($"Bid messages: {bidMessageCount}");
            Debug.Log($"Evaluations: {evaluationCount}");
            Debug.Log($"Confirm messages: {confirmMessageCount}");
            Debug.Log($"Reassign messages: {reassignMessageCount}");
            Debug.Log($"Total messages: {totalMessageCount}");
            Debug.Log($"Tasks needing reassignment: {tasksNeedingReassignment}");
            Debug.Log($"Successful reassignments: {successfulReassignments}");
            Debug.Log($"Failed reassignments: {failedReassignments}");
            Debug.Log($"Average total task delay: {GetAverageTaskLifecycleTime():F2} s");
            Debug.Log($"Average auction-to-confirm delay: {GetAverageAuctionToConfirmDelay():F2} s");
            Debug.Log($"Average confirm-to-execution delay: {GetAverageConfirmToExecutionDelay():F2} s");
            Debug.Log($"Average execution duration: {GetAverageExecutionDuration():F2} s");

            if (ExperimentLogger.Instance != null)
            {
                ExperimentLogger.Instance.LogSummary(this);
            }

            if (experimentRunner != null)
            {
                experimentRunner.OnSingleRunFinished();
            }
        }
    }

    // =========================
    // 节点失效 / 断连
    // =========================
    void CheckFailInput()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            FailAgentByIndex(0);
        }
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            FailAgentByIndex(1);
        }
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            FailAgentByIndex(2);
        }

        if (Input.GetKeyDown(KeyCode.Q))
        {
            DisconnectAgentByIndex(0);
        }
        if (Input.GetKeyDown(KeyCode.W))
        {
            DisconnectAgentByIndex(1);
        }
        if (Input.GetKeyDown(KeyCode.E))
        {
            DisconnectAgentByIndex(2);
        }
    }

    void FailAgentByIndex(int index)
    {
        if (agents == null || index < 0 || index >= agents.Length) return;
        if (agents[index] == null) return;
        if (agents[index].isFailed) return;

        currentTriggerReason = "AGENT_FAILED";

        agents[index].FailAgent();
        failedAgentCount++;

        Debug.Log($"Failed agent count: {failedAgentCount}");
    }

    void DisconnectAgentByIndex(int index)
    {
        if (!enableAgentDisconnection) return;
        if (agents == null || index < 0 || index >= agents.Length) return;
        if (agents[index] == null) return;
        if (agents[index].isFailed) return;
        if (agents[index].isDisconnected) return;

        currentTriggerReason = "AGENT_DISCONNECTED";
        agents[index].DisconnectTemporarily(disconnectionDuration);

        Debug.Log($"Agent {agents[index].agentId} temporarily disconnected.");
    }

    // =========================
    // 对外统计接口
    // =========================
    public float GetCompletionRate()
    {
        if (totalTaskCount == 0) return 0f;
        return (float)completedTaskCount / totalTaskCount;
    }

    public float GetTotalCompletionTime()
    {
        if (allTasksFinished)
            return endTime - startTime;

        return Time.time - startTime;
    }

    public float GetFairnessIndex()
    {
        if (agents == null || agents.Length == 0) return 0f;

        float sum = 0f;
        float sumSquare = 0f;
        int validAgentCount = 0;

        foreach (AgentController agent in agents)
        {
            if (agent == null) continue;

            float x = agent.completedTaskCount;
            sum += x;
            sumSquare += x * x;
            validAgentCount++;
        }

        if (validAgentCount == 0 || sumSquare == 0f) return 0f;

        return (sum * sum) / (validAgentCount * sumSquare);
    }

    public float GetReassignmentSuccessRate()
    {
        if (tasksNeedingReassignment == 0) return 0f;
        return (float)successfulReassignments / tasksNeedingReassignment;
    }

    public float GetAverageTaskLifecycleTime()
    {
        float sum = 0f;
        int count = 0;

        foreach (TaskPoint task in tasks)
        {
            if (task == null) continue;

            float value = task.GetTotalLifecycleTime();
            if (value >= 0f)
            {
                sum += value;
                count++;
            }
        }

        if (count == 0) return 0f;
        return sum / count;
    }

    public float GetAverageAuctionToConfirmDelay()
    {
        float sum = 0f;
        int count = 0;

        foreach (TaskPoint task in tasks)
        {
            if (task == null) continue;

            float value = task.GetAuctionToConfirmDelay();
            if (value >= 0f)
            {
                sum += value;
                count++;
            }
        }

        if (count == 0) return 0f;
        return sum / count;
    }

    public float GetAverageConfirmToExecutionDelay()
    {
        float sum = 0f;
        int count = 0;

        foreach (TaskPoint task in tasks)
        {
            if (task == null) continue;

            float value = task.GetExecutionDelay();
            if (value >= 0f)
            {
                sum += value;
                count++;
            }
        }

        if (count == 0) return 0f;
        return sum / count;
    }

    public float GetAverageExecutionDuration()
    {
        float sum = 0f;
        int count = 0;

        foreach (TaskPoint task in tasks)
        {
            if (task == null) continue;

            float value = task.GetExecutionDuration();
            if (value >= 0f)
            {
                sum += value;
                count++;
            }
        }

        if (count == 0) return 0f;
        return sum / count;
    }
}