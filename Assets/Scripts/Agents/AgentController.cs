using UnityEngine; // 引入 Unity 引擎命名空间，使用 MonoBehaviour、Vector3、Color、Debug 等类型

// 定义智能体类型枚举
// 用于区分当前智能体属于哪一类：工人、无人机、车辆
public enum AgentType
{
    Worker, // 工人
    Drone,  // 无人机
    Car     // 车辆
}

// AgentController 表示“单个智能体”的控制脚本
// 每一个挂载了该脚本的游戏对象，都是一个可参与拍卖和执行任务的智能体
public class AgentController : MonoBehaviour
{
    [Header("Basic Info")] // 在 Inspector 面板中显示分组标题：基础信息

    public int agentId; // 智能体编号，用于区分不同 Agent，例如 0、1、2
    public AgentType agentType = AgentType.Worker; // 智能体类型，默认是 Worker
    public float moveSpeed = 3f; // 智能体移动速度，数值越大移动越快

    [Header("Capability")] // Inspector 中显示分组标题：能力参数

    public float capabilityScore = 1f;   // 智能体基础能力值，越高说明整体执行能力越强，在计算 bid 时会降低代价
    public float riskTolerance = 1f;     // 风险承受能力，越高越适合高风险任务，风险代价会更低
    public int maxLoad = 2;              // 最大承载上限，表示该智能体最多允许同时承担多少任务（当前版本通常一次一个，但这里预留扩展）

    [Header("Load State")] // Inspector 中显示分组标题：负载状态

    public int currentLoad = 0; // 当前负载数量，表示智能体当前已经承担的任务数

    [Header("Runtime State")] // Inspector 中显示分组标题：运行状态

    public int completedTaskCount = 0; // 该智能体累计完成的任务数
    public bool isFailed = false;      // 是否已经失效，true 表示该智能体故障/失效，不再参与任务

    [Header("Network State")] // Inspector 中显示分组标题：网络状态

    public bool isDisconnected = false;    // 是否处于临时断连状态，true 表示暂时无法通信/参与协同
    public float disconnectEndTime = -1f;  // 断连结束时间点（Time.time 的绝对时间），到达后自动恢复连接

    [Header("Auction Role State")] // Inspector 中显示分组标题：拍卖角色状态

    public bool isProposedWinner = false;   // 是否被评标阶段选为“临时赢家”
    public bool hasConfirmedTask = false;   // 是否已经在共识完成后正式确认获得任务

    [Header("Soft Match Settings")] // Inspector 中显示分组标题：软匹配设置

    public bool useSoftTypeConstraint = true; // 是否启用“软类型约束”
                                              // true：类型不匹配时仍允许投标，但会加惩罚
                                              // false：类型不匹配时直接禁止投标（硬约束）

    public float typeMismatchPenalty = 10f;   // 类型不匹配时增加的惩罚代价，数值越大越不容易中标

    private TaskPoint currentTask; // 当前智能体正在处理或已确认接收的任务对象
    private bool isMoving = false; // 当前是否正在移动执行任务

    private GameManager gm; // 对 GameManager 的引用，用于任务完成回调、尝试重新分配任务等

    // 记录初始位置与朝向，供重置使用
    private Vector3 initialPosition;   // 智能体初始位置
    private Quaternion initialRotation; // 智能体初始朝向

    // 外观控制
    private Renderer agentRenderer;         // 智能体渲染器，用来修改材质颜色
    private Color initialColor = Color.white; // 智能体初始颜色，默认白色，启动时会读取真实材质颜色覆盖它

    void Awake()
    {
        gm = FindObjectOfType<GameManager>();

        initialPosition = transform.position;
        initialRotation = transform.rotation;

        agentRenderer = GetComponentInChildren<Renderer>();
        if (agentRenderer != null && agentRenderer.material != null)
        {
            initialColor = agentRenderer.material.color;
        }

        Debug.Log($"Agent {agentId} Awake -> initialPosition = {initialPosition}");
    }

    // 判断当前智能体是否忙碌
    // 只要 currentTask 不为空，就说明它当前已经有任务在身
    public bool IsBusy()
    {
        return currentTask != null;
    }

    // 判断智能体在一般条件下是否可以参与投标
    public bool CanBid()
    {
        // 必须同时满足：
        // 1. 没有失效
        // 2. 没有断连
        // 3. 当前不忙
        // 4. 当前负载小于最大负载
        return !isFailed && !isDisconnected && !IsBusy() && currentLoad < maxLoad;
    }

    // 判断智能体是否可以对“某个具体任务”投标
    public bool CanBid(TaskPoint task)
    {
        // 如果任务为空，直接不能投标
        if (task == null) return false;

        // 如果基本状态都不允许投标，直接返回 false
        if (!CanBid()) return false;

        // 如果任务已经完成，就没有必要投标了
        if (task.isCompleted) return false;

        // 软约束逻辑：
        // 当 useSoftTypeConstraint = false 时，表示启用“硬约束”
        // 也就是任务要求类型必须与智能体类型完全一致，否则不能投标
        if (!useSoftTypeConstraint)
        {
            if (task.requiredAgentType != agentType)
            {
                return false;
            }
        }

        // 能走到这里，说明允许投标
        return true;
    }

    // 计算当前智能体对某个任务的出价（bid）
    // bid 越小，通常表示越适合承担该任务
    public float CalculateBid(TaskPoint task)
    {
        // 若任务为空，返回极大值，表示无效出价
        if (task == null) return float.MaxValue;

        // 若当前不允许对该任务投标，也返回极大值
        if (!CanBid(task)) return float.MaxValue;

        // 计算智能体当前位置到任务位置的欧氏距离
        float distance = Vector3.Distance(transform.position, task.transform.position);

        // 1. 距离代价
        // 距离越远，代价越大
        float distanceCost = distance;

        // 2. 当前负载代价
        // 当前承担任务越多，代价越大
        float loadCost = 2f * currentLoad;

        // 3. 风险代价
        // 任务风险越高、智能体风险容忍度越低，则代价越大
        // Mathf.Max(0.1f, riskTolerance) 是为了防止除以 0 或过小数导致异常
        float safeRiskTolerance = Mathf.Max(0.1f, riskTolerance);
        float riskCost = task.riskLevel / safeRiskTolerance;

        // 4. 紧急任务的类型偏好调整
        // 这里通过不同 agentType 在紧急任务下给不同修正值
        // 负值表示更有优势，正值表示相对不占优
        float urgencyAdjustment = 0f;

        if (task.isUrgent)
        {
            switch (agentType)
            {
                case AgentType.Worker:
                    // 工人在紧急任务中略微不占优
                    urgencyAdjustment = 0.8f;
                    break;

                case AgentType.Drone:
                    // 无人机在紧急任务中更占优，因此减小 bid
                    urgencyAdjustment = -2.2f;
                    break;

                case AgentType.Car:
                    // 车辆在紧急任务中也有一定优势
                    urgencyAdjustment = -1.0f;
                    break;
            }
        }

        // 5. 类型匹配收益 / 不匹配惩罚
        // 若任务类型与智能体类型一致，则给予匹配奖励
        bool isTypeMatched = task.requiredAgentType == agentType;

        float typeMatchBonus = isTypeMatched ? 2.5f : 0f; // 类型匹配则减小 bid
        float mismatchPenalty = (!isTypeMatched && useSoftTypeConstraint) ? typeMismatchPenalty : 0f;
        // 若启用了软约束并且类型不匹配，则增加惩罚

        // 6. 能力收益
        // capabilityScore 越高，说明能力越强，因此在总 bid 中作为“减项”
        float capabilityBonus = capabilityScore;

        // 7. 异构偏好修正
        // 根据不同智能体的特点，对距离、风险、紧急性做进一步微调
        float heterogeneousAdjustment = 0f;

        switch (agentType)
        {
            case AgentType.Worker:
                // 工人更适合近距离、常规风险任务

                if (distance <= 3f)
                {
                    // 如果任务较近，工人更适合，降低 bid
                    heterogeneousAdjustment -= 1.0f;
                }

                if (task.riskLevel >= 4f)
                {
                    // 若风险过高，工人相对不适合，提高 bid
                    heterogeneousAdjustment += 1.2f;
                }
                break;

            case AgentType.Drone:
                // 无人机更适合远距离、紧急、高风险任务

                if (distance >= 3f)
                {
                    // 距离越远，无人机更有优势，降低 bid
                    heterogeneousAdjustment -= 1.2f;
                }

                if (task.riskLevel >= 3.5f)
                {
                    // 高风险任务，无人机更有优势，降低 bid
                    heterogeneousAdjustment -= 1.0f;
                }

                if (task.isUrgent)
                {
                    // 紧急任务，无人机更适合，进一步降低 bid
                    heterogeneousAdjustment -= 0.8f;
                }
                break;

            case AgentType.Car:
                // 车辆更适合中远距离、较稳定通行任务

                if (distance >= 2.5f)
                {
                    // 中远距离任务车辆较有优势
                    heterogeneousAdjustment -= 0.8f;
                }

                if (task.riskLevel >= 4.5f)
                {
                    // 极高风险任务下车辆可能受限，增加代价
                    heterogeneousAdjustment += 0.8f;
                }
                break;
        }

        // 最终 bid 计算公式
        // 注意：
        // cost / penalty 一般是加项（让 bid 变大）
        // bonus 一般是减项（让 bid 变小）
        float bid =
            distanceCost +            // 距离代价
            loadCost +                // 负载代价
            riskCost +                // 风险代价
            urgencyAdjustment -       // 紧急性修正（可能正也可能负）
            typeMatchBonus -          // 类型匹配奖励
            capabilityBonus +         // 能力收益（作为减项）
            heterogeneousAdjustment + // 异构偏好修正
            mismatchPenalty;          // 类型不匹配惩罚

        // 返回最终出价，数值越小越优
        return bid;
    }

    /// <summary>
    /// 仅表示该智能体在评标阶段被选为“临时赢家”
    /// 注意：这一步不代表正式获得任务，也不会立即开始移动
    /// 它只是进入“待确认”状态，等待后续共识确认
    /// </summary>
    public void SetAsProposedWinner(TaskPoint task)
    {
        // 若任务为空、智能体已失效、智能体已断连，则不能成为临时赢家
        if (task == null || isFailed || isDisconnected) return;

        // 若任务已经完成，也不需要设置临时赢家
        if (task.isCompleted) return;

        // 标记当前智能体为临时赢家
        isProposedWinner = true;

        // 输出调试信息
        Debug.Log($"Agent {agentId} ({agentType}) is proposed winner for Task {task.taskId}");
    }

    /// <summary>
    /// 共识成功后，正式接收任务
    /// 这一步才表示任务真正分配给该智能体
    /// </summary>
    public void AssignTask(TaskPoint task)
    {
        // 防御式判断：任务为空或当前智能体不可用，则直接返回
        if (task == null || isFailed || isDisconnected) return;

        // 若任务已经完成，无需再次分配
        if (task.isCompleted) return;

        // 若任务还没有确认赢家，则不能正式分配
        if (!task.HasConfirmedWinner()) return;

        // 若任务确认赢家不是当前智能体，则当前智能体不能接收该任务
        if (task.confirmedWinnerId != agentId) return;

        // 将当前任务记录为自己的任务
        currentTask = task;

        // 此处先不立即移动
        // 因为系统设计为：先正式分配，再由 StartExecution() 启动执行
        isMoving = false;

        // 不再是临时赢家，因为已经转为正式确认赢家
        isProposedWinner = false;

        // 标记该任务已经被正式确认给当前智能体
        hasConfirmedTask = true;

        // 当前负载 +1，但保证不超过 maxLoad
        currentLoad = Mathf.Clamp(currentLoad + 1, 0, maxLoad);

        // 输出调试日志
        Debug.Log($"Agent {agentId} ({agentType}) confirmed assignment for Task {task.taskId}");
    }

    /// <summary>
    /// 正式开始执行任务
    /// 只有已经确认分配过的任务，才能进入执行阶段
    /// </summary>
    public void StartExecution()
    {
        // 若智能体已失效或已断连，不能执行任务
        if (isFailed || isDisconnected) return;

        // 若没有当前任务，不能执行
        if (currentTask == null) return;

        // 若还没有正式确认任务，也不能执行
        if (!hasConfirmedTask) return;

        // 启动移动执行
        isMoving = true;

        // 通知任务对象：进入执行状态
        currentTask.StartExecution();

        // 输出调试日志
        Debug.Log($"Agent {agentId} ({agentType}) started executing Task {currentTask.taskId}");
    }

    void Update()
    {
        // 每帧先检查是否需要从断连状态恢复
        HandleDisconnectionRecovery();

        // 如果智能体已经失效，直接停止后续逻辑
        if (isFailed) return;

        // 如果当前断连，也不执行移动逻辑
        if (isDisconnected) return;

        // 如果当前没有在移动，或者没有任务，则不执行后续移动逻辑
        if (!isMoving || currentTask == null) return;

        // 获取任务位置，作为移动目标点
        Vector3 targetPos = currentTask.transform.position;

        // 计算从当前位置指向目标位置的方向向量
        Vector3 dir = targetPos - transform.position;

        // 如果方向不为零，说明当前位置与目标位置不同
        if (dir != Vector3.zero)
        {
            // 根据方向向量生成目标朝向
            Quaternion targetRot = Quaternion.LookRotation(dir);

            // 使用球面插值让旋转更平滑，不会瞬间转向
            transform.rotation = Quaternion.Slerp(
                transform.rotation,     // 当前朝向
                targetRot,              // 目标朝向
                5f * Time.deltaTime     // 插值速度
            );
        }

        // 让当前位置以 moveSpeed 速度朝目标位置移动
        transform.position = Vector3.MoveTowards(
            transform.position,        // 当前位置
            targetPos,                 // 目标位置
            moveSpeed * Time.deltaTime // 本帧移动距离
        );

        // 计算当前与目标点的距离
        float dist = Vector3.Distance(transform.position, targetPos);

        // 如果距离足够小，认为已经到达任务点
        if (dist < 0.2f)
        {
            // 将任务标记为完成
            currentTask.MarkCompleted();

            // 当前智能体的完成任务计数 +1
            completedTaskCount += 1;

            // 输出日志
            Debug.Log($"Agent {agentId} ({agentType}) completed Task {currentTask.taskId}");

            // 若存在 GameManager，则通知系统“某任务已完成”
            if (gm != null)
            {
                gm.OnTaskCompleted(this, currentTask);
            }

            // 清理当前任务相关运行状态
            ClearTaskRuntimeState();

            // 完成后尝试继续分配剩余任务
            if (gm != null)
            {
                gm.TryAssignRemainingTasks();
            }
        }
    }

    // 处理断连恢复逻辑
    // 每帧检查：如果处于断连中，并且断连结束时间已到，则自动恢复连接
    void HandleDisconnectionRecovery()
    {
        // 如果当前并未断连，则无需处理
        if (!isDisconnected) return;

        // 如果结束时间非法（<0），说明没有设置有效恢复时间
        if (disconnectEndTime < 0f) return;

        // 如果当前时间已经超过断连结束时间
        if (Time.time >= disconnectEndTime)
        {
            // 自动恢复连接
            RecoverConnection();
        }
    }

    // 让智能体临时断连一段时间
    public void DisconnectTemporarily(float duration)
    {
        // 若已经失效，不再允许断连逻辑
        if (isFailed) return;

        // 若本来就已断连，则不重复处理
        if (isDisconnected) return;

        // 标记为断连状态
        isDisconnected = true;

        // 设置断连结束时间 = 当前时间 + 持续时长
        disconnectEndTime = Time.time + duration;

        // 断连后停止移动
        isMoving = false;

        // 输出调试日志
        Debug.Log($"Agent {agentId} ({agentType}) DISCONNECTED for {duration:F1}s.");

        // 若有渲染器，则将颜色改为黄色，表示临时断连
        if (agentRenderer != null && agentRenderer.material != null)
        {
            agentRenderer.material.color = Color.yellow;
        }

        // 这里没有自动释放任务
        // 原因是当前设计希望由上层的“共识超时 / 重拍逻辑”决定是否回收任务
        // 这样更符合分布式拍卖实验的表达
    }

    // 恢复连接
    public void RecoverConnection()
    {
        // 若本来就不处于断连状态，则不需要恢复
        if (!isDisconnected) return;

        // 断连标记清除
        isDisconnected = false;

        // 重置断连结束时间
        disconnectEndTime = -1f;

        // 输出日志
        Debug.Log($"Agent {agentId} ({agentType}) RECONNECTED.");

        // 恢复材质颜色为初始颜色
        if (agentRenderer != null && agentRenderer.material != null)
        {
            agentRenderer.material.color = initialColor;
        }

        // 恢复后尝试重新参与系统中的剩余任务分配
        if (!isFailed && gm != null)
        {
            gm.TryAssignRemainingTasks();
        }
    }

    // 让智能体永久失效
    public void FailAgent()
    {
        // 如果已经失效，则不重复处理
        if (isFailed) return;

        // 标记为失效
        isFailed = true;

        // 失效后不再视为断连，而是彻底不可用
        isDisconnected = false;

        // 清除断连恢复时间
        disconnectEndTime = -1f;

        // 停止移动
        isMoving = false;

        // 输出日志
        Debug.Log($"Agent {agentId} ({agentType}) FAILED.");

        // 若存在渲染器，将颜色改为灰色，表示失效
        if (agentRenderer != null && agentRenderer.material != null)
        {
            agentRenderer.material.color = Color.gray;
        }

        // 释放当前任务，交由系统重新拍卖/重分配
        ReleaseCurrentTask();

        // 通知系统尝试重新分配剩余任务
        if (gm != null)
        {
            gm.TryAssignRemainingTasks();
        }
    }

    // 释放当前任务
    // 通常在智能体失效时使用
    private void ReleaseCurrentTask()
    {
        // 如果当前确实持有任务，并且任务尚未完成
        if (currentTask != null && !currentTask.isCompleted)
        {
            // 输出日志
            Debug.Log($"Agent {agentId} ({agentType}) released Task {currentTask.taskId}");

            // 让任务重新进入拍卖状态
            currentTask.EnterReauction();

            // 清空当前任务引用
            currentTask = null;
        }

        // 释放任务后，把负载清零
        currentLoad = 0;

        // 停止移动
        isMoving = false;

        // 清空拍卖相关状态
        isProposedWinner = false;
        hasConfirmedTask = false;
    }

    // 清理当前任务的运行时状态
    // 通常在任务完成后调用
    private void ClearTaskRuntimeState()
    {
        currentTask = null;                   // 清空当前任务引用
        isMoving = false;                     // 停止移动
        isProposedWinner = false;             // 清除临时赢家状态
        hasConfirmedTask = false;             // 清除正式确认状态
        currentLoad = Mathf.Max(0, currentLoad - 1); // 当前负载减 1，但最低不小于 0
    }

    // 重置智能体状态
    // 通常在“重新开始实验 / 多轮实验重置”时调用
    public void ResetAgentState()
    {
        currentLoad = 0;            // 重置当前负载
        completedTaskCount = 0;     // 重置完成任务计数
        isFailed = false;           // 清除失效状态
        isDisconnected = false;     // 清除断连状态
        disconnectEndTime = -1f;    // 清除断连结束时间

        currentTask = null;         // 清空当前任务
        isMoving = false;           // 停止移动
        isProposedWinner = false;   // 清除临时赢家状态
        hasConfirmedTask = false;   // 清除正式确认状态

        // 恢复到初始位置
        transform.position = initialPosition;

        // 恢复到初始朝向
        transform.rotation = initialRotation;

        // 恢复为初始颜色
        if (agentRenderer != null && agentRenderer.material != null)
        {
            agentRenderer.material.color = initialColor;
        }

        // 输出调试日志
        Debug.Log($"Agent {agentId} ({agentType}) reset to initial state.");
    }
}