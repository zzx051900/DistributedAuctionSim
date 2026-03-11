using UnityEngine;

public class TaskPoint : MonoBehaviour
{
    public enum TaskState
    {
        Idle,
        Bidding,
        Evaluating,
        Confirming,
        Committed,
        Executing,
        Completed,
        Reauctioning
    }

    public enum ConsensusResult
    {
        None,
        Pending,
        Success,
        Timeout,
        ProposedAgentFailed,
        Conflict,
        Cancelled
    }

    [Header("Basic Info")]
    public int taskId;

    [Header("Heterogeneous Task Info")]
    public AgentType requiredAgentType = AgentType.Worker;
    public float riskLevel = 1f;
    public bool isUrgent = false;

    [Header("Legacy Flags")]
    public bool isAssigned = false;
    public bool isCompleted = false;

    [Header("Auction State")]
    public TaskState currentState = TaskState.Idle;
    public int auctionRound = 0;

    [Header("Winner Info")]
    public int proposedWinnerId = -1;
    public int confirmedWinnerId = -1;

    [Header("Consensus Info")]
    public int confirmCount = 0;
    public int requiredConfirmations = 1;
    public float consensusStartTime = -1f;
    public float consensusTimeout = 2f;
    public bool consensusSucceeded = false;

    [Header("Consensus Extended Info")]
    public ConsensusResult consensusResult = ConsensusResult.None;
    public int consensusVersion = 0;
    public int lastConfirmedByAgentId = -1;
    public bool consensusLocked = false;

    [Header("Time Statistics")]
    public float taskCreateTime = -1f;
    public float currentRoundBiddingStartTime = -1f;
    public float evaluationStartTime = -1f;
    public float proposalTime = -1f;
    public float assignmentConfirmTime = -1f;
    public float executionStartTime = -1f;
    public float completionTime = -1f;

    [Header("Reassignment Statistics")]
    public bool hasBeenReassigned = false;
    public int reassignCount = 0;
    public int firstAssignedAgentId = -1;
    public int latestAssignedAgentId = -1;

    private Renderer rend;

    void Start()
    {
        rend = GetComponent<Renderer>();

        if (taskCreateTime < 0f)
        {
            taskCreateTime = Time.time;
        }

        UpdateColor();
    }

    public void StartAuctionRound(int newRound)
    {
        if (isCompleted) return;

        auctionRound = newRound;
        currentState = TaskState.Bidding;

        isAssigned = false;
        proposedWinnerId = -1;
        confirmedWinnerId = -1;

        confirmCount = 0;
        consensusStartTime = -1f;
        consensusSucceeded = false;
        consensusResult = ConsensusResult.Pending;
        lastConfirmedByAgentId = -1;
        consensusLocked = false;
        consensusVersion++;

        currentRoundBiddingStartTime = Time.time;
        evaluationStartTime = -1f;
        proposalTime = -1f;
        assignmentConfirmTime = -1f;
        executionStartTime = -1f;

        UpdateColor();
    }

    public void EnterEvaluating()
    {
        if (isCompleted) return;
        if (currentState == TaskState.Completed || currentState == TaskState.Executing) return;

        currentState = TaskState.Evaluating;

        if (evaluationStartTime < 0f)
        {
            evaluationStartTime = Time.time;
        }

        UpdateColor();
    }

    public void SetProposedWinner(int agentId, int requiredConfirms = 1, float timeout = 2f)
    {
        if (isCompleted) return;
        if (agentId < 0) return;

        proposedWinnerId = agentId;
        confirmedWinnerId = -1;

        requiredConfirmations = Mathf.Max(1, requiredConfirms);
        consensusTimeout = Mathf.Max(0.1f, timeout);

        confirmCount = 0;
        consensusStartTime = Time.time;
        consensusSucceeded = false;
        consensusResult = ConsensusResult.Pending;
        lastConfirmedByAgentId = -1;
        consensusLocked = false;

        proposalTime = Time.time;
        currentState = TaskState.Confirming;

        UpdateColor();
    }

    public bool AddConfirmation()
    {
        if (isCompleted) return false;
        if (currentState != TaskState.Confirming) return false;
        if (consensusLocked) return false;
        if (proposedWinnerId < 0) return false;

        confirmCount++;

        if (confirmCount >= requiredConfirmations)
        {
            ConfirmWinner();
        }

        return true;
    }

    public bool AddConfirmationFromAgent(int confirmerAgentId)
    {
        if (isCompleted) return false;
        if (currentState != TaskState.Confirming) return false;
        if (consensusLocked) return false;
        if (proposedWinnerId < 0) return false;

        lastConfirmedByAgentId = confirmerAgentId;
        return AddConfirmation();
    }

    public void ConfirmWinner()
    {
        if (isCompleted) return;
        if (currentState != TaskState.Confirming && currentState != TaskState.Committed) return;
        if (proposedWinnerId < 0) return;
        if (consensusLocked && consensusSucceeded) return;

        confirmedWinnerId = proposedWinnerId;
        consensusSucceeded = true;
        consensusResult = ConsensusResult.Success;
        consensusLocked = true;
        currentState = TaskState.Committed;

        isAssigned = true;
        assignmentConfirmTime = Time.time;
        latestAssignedAgentId = confirmedWinnerId;

        if (firstAssignedAgentId < 0)
        {
            firstAssignedAgentId = confirmedWinnerId;
        }

        UpdateColor();
    }

    public void MarkConsensusTimeout()
    {
        if (isCompleted) return;
        if (currentState != TaskState.Confirming) return;

        consensusSucceeded = false;
        consensusResult = ConsensusResult.Timeout;
        consensusLocked = false;
    }

    public void MarkConsensusFailedByAgentFailure()
    {
        if (isCompleted) return;

        consensusSucceeded = false;
        consensusResult = ConsensusResult.ProposedAgentFailed;
        consensusLocked = false;
    }

    public void MarkConsensusConflict()
    {
        if (isCompleted) return;

        consensusSucceeded = false;
        consensusResult = ConsensusResult.Conflict;
        consensusLocked = false;
    }

    public void CancelConsensus()
    {
        if (isCompleted) return;

        consensusSucceeded = false;
        consensusResult = ConsensusResult.Cancelled;
        consensusLocked = false;

        proposedWinnerId = -1;
        confirmedWinnerId = -1;
        confirmCount = 0;
        consensusStartTime = -1f;

        if (!isAssigned)
        {
            currentState = TaskState.Idle;
        }

        UpdateColor();
    }

    public void StartExecution()
    {
        if (isCompleted) return;
        if (!isAssigned) return;
        if (confirmedWinnerId < 0) return;

        currentState = TaskState.Executing;

        if (executionStartTime < 0f)
        {
            executionStartTime = Time.time;
        }

        UpdateColor();
    }

    public void MarkAssigned()
    {
        if (isCompleted) return;

        isAssigned = true;
        currentState = TaskState.Committed;

        if (assignmentConfirmTime < 0f)
        {
            assignmentConfirmTime = Time.time;
        }

        if (confirmedWinnerId >= 0)
        {
            latestAssignedAgentId = confirmedWinnerId;

            if (firstAssignedAgentId < 0)
            {
                firstAssignedAgentId = confirmedWinnerId;
            }
        }

        UpdateColor();
    }

    public void MarkCompleted()
    {
        if (isCompleted) return;

        isCompleted = true;
        isAssigned = false;
        currentState = TaskState.Completed;

        if (completionTime < 0f)
        {
            completionTime = Time.time;
        }

        UpdateColor();
    }

    public void ReleaseTask()
    {
        if (isCompleted) return;

        isAssigned = false;
        proposedWinnerId = -1;
        confirmedWinnerId = -1;
        confirmCount = 0;
        consensusStartTime = -1f;
        consensusSucceeded = false;
        consensusResult = ConsensusResult.None;
        lastConfirmedByAgentId = -1;
        consensusLocked = false;

        assignmentConfirmTime = -1f;
        executionStartTime = -1f;

        currentState = TaskState.Idle;
        UpdateColor();
    }

    public void EnterReauction()
    {
        if (isCompleted) return;

        hasBeenReassigned = true;
        reassignCount++;

        isAssigned = false;
        proposedWinnerId = -1;
        confirmedWinnerId = -1;
        confirmCount = 0;
        consensusStartTime = -1f;
        consensusSucceeded = false;
        lastConfirmedByAgentId = -1;
        consensusLocked = false;

        assignmentConfirmTime = -1f;
        executionStartTime = -1f;

        if (consensusResult == ConsensusResult.None || consensusResult == ConsensusResult.Pending)
        {
            consensusResult = ConsensusResult.Cancelled;
        }

        currentState = TaskState.Reauctioning;
        UpdateColor();
    }

    public void ResetTask()
    {
        isAssigned = false;
        isCompleted = false;

        currentState = TaskState.Idle;
        auctionRound = 0;

        proposedWinnerId = -1;
        confirmedWinnerId = -1;

        confirmCount = 0;
        requiredConfirmations = 1;
        consensusStartTime = -1f;
        consensusTimeout = 2f;
        consensusSucceeded = false;

        consensusResult = ConsensusResult.None;
        consensusVersion = 0;
        lastConfirmedByAgentId = -1;
        consensusLocked = false;

        taskCreateTime = Time.time;
        currentRoundBiddingStartTime = -1f;
        evaluationStartTime = -1f;
        proposalTime = -1f;
        assignmentConfirmTime = -1f;
        executionStartTime = -1f;
        completionTime = -1f;

        hasBeenReassigned = false;
        reassignCount = 0;
        firstAssignedAgentId = -1;
        latestAssignedAgentId = -1;

        UpdateColor();
    }

    public bool IsConsensusTimeout()
    {
        if (currentState != TaskState.Confirming) return false;
        if (consensusStartTime < 0f) return false;

        return Time.time - consensusStartTime > consensusTimeout;
    }

    public bool CanReauction()
    {
        return !isCompleted &&
               currentState != TaskState.Completed &&
               currentState != TaskState.Executing;
    }

    public bool HasProposedWinner()
    {
        return proposedWinnerId >= 0;
    }

    public bool HasConfirmedWinner()
    {
        return confirmedWinnerId >= 0;
    }

    public bool IsConsensusPending()
    {
        return currentState == TaskState.Confirming && !consensusSucceeded;
    }

    public float GetTotalLifecycleTime()
    {
        if (taskCreateTime < 0f || completionTime < 0f) return -1f;
        return completionTime - taskCreateTime;
    }

    public float GetAuctionToConfirmDelay()
    {
        if (currentRoundBiddingStartTime < 0f || assignmentConfirmTime < 0f) return -1f;
        return assignmentConfirmTime - currentRoundBiddingStartTime;
    }

    public float GetEvaluationDelay()
    {
        if (currentRoundBiddingStartTime < 0f || evaluationStartTime < 0f) return -1f;
        return evaluationStartTime - currentRoundBiddingStartTime;
    }

    public float GetProposalDelay()
    {
        if (evaluationStartTime < 0f || proposalTime < 0f) return -1f;
        return proposalTime - evaluationStartTime;
    }

    public float GetConsensusDuration()
    {
        if (proposalTime < 0f || assignmentConfirmTime < 0f) return -1f;
        return assignmentConfirmTime - proposalTime;
    }

    public float GetExecutionDelay()
    {
        if (assignmentConfirmTime < 0f || executionStartTime < 0f) return -1f;
        return executionStartTime - assignmentConfirmTime;
    }

    public float GetExecutionDuration()
    {
        if (executionStartTime < 0f || completionTime < 0f) return -1f;
        return completionTime - executionStartTime;
    }

    public string GetStateText()
    {
        return currentState.ToString();
    }

    public string GetTaskTypeText()
    {
        return requiredAgentType.ToString();
    }

    public string GetConsensusResultText()
    {
        return consensusResult.ToString();
    }

    private void UpdateColor()
    {
        if (rend == null) return;

        switch (currentState)
        {
            case TaskState.Idle:
                rend.material.color = Color.red;
                break;

            case TaskState.Bidding:
                rend.material.color = new Color(1f, 0.5f, 0f);
                break;

            case TaskState.Evaluating:
                rend.material.color = Color.magenta;
                break;

            case TaskState.Confirming:
                rend.material.color = Color.cyan;
                break;

            case TaskState.Committed:
                rend.material.color = Color.yellow;
                break;

            case TaskState.Executing:
                rend.material.color = new Color(0.3f, 0.7f, 1f);
                break;

            case TaskState.Completed:
                rend.material.color = Color.green;
                break;

            case TaskState.Reauctioning:
                rend.material.color = Color.gray;
                break;

            default:
                rend.material.color = Color.white;
                break;
        }
    }
}