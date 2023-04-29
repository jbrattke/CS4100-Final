//Put this script on your blue cube.

using System.Collections;
using UnityEngine;
using Unity.MLAgents;
using Unity.Barracuda;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Unity.MLAgentsExamples;

public class WallJumpAgent : Agent
{
    public GameObject ground;

    public GameObject goal;
    Rigidbody m_AgentRb;
    Material m_GroundMaterial;
    Renderer m_GroundRenderer;
    WallJumpSettings m_WallJumpSettings;

    public float jumpingTime;
    public float jumpTime;
    // This is a downward force applied when falling to make jumps look
    // less floaty
    public float fallingForce;
    // Use to check the coliding objects
    public Collider[] hitGroundColliders = new Collider[3];
    Vector3 m_JumpTargetPos;
    Vector3 m_JumpStartingPos;

    EnvironmentParameters m_ResetParams;

    public float m_EnvironmentTimeout = 30.0f;
    float m_TimeoutStartTime;

    int[,] m_VisitedStates = new int[25, 25];

    public override void Initialize()
    {
        m_WallJumpSettings = FindObjectOfType<WallJumpSettings>();

        m_AgentRb = GetComponent<Rigidbody>();
        m_GroundRenderer = ground.GetComponent<Renderer>();
        m_GroundMaterial = m_GroundRenderer.material;

        m_ResetParams = Academy.Instance.EnvironmentParameters;

        m_TimeoutStartTime = Time.time;
    }

    // Begin the jump sequence
    public void Jump()
    {
        jumpingTime = 0.2f;
        m_JumpStartingPos = m_AgentRb.position;
    }

    public bool DoGroundCheck(bool smallCheck)
    {
        if (!smallCheck)
        {
            hitGroundColliders = new Collider[3];
            var o = gameObject;
            Physics.OverlapBoxNonAlloc(
                o.transform.position + new Vector3(0, -0.05f, 0),
                new Vector3(0.95f / 2f, 0.5f, 0.95f / 2f),
                hitGroundColliders,
                o.transform.rotation);
            var grounded = false;
            foreach (var col in hitGroundColliders)
            {
                if (col != null && col.transform != transform &&
                    (col.CompareTag("walkableSurface") ||
                     col.CompareTag("block") ||
                     col.CompareTag("wall")))
                {
                    grounded = true; //then we're grounded
                    break;
                }
            }
            return grounded;
        }
        else
        {
            RaycastHit hit;
            Physics.Raycast(transform.position + new Vector3(0, -0.05f, 0), -Vector3.up, out hit,
                1f);

            if (hit.collider != null &&
                (hit.collider.CompareTag("walkableSurface") ||
                 hit.collider.CompareTag("block") ||
                 hit.collider.CompareTag("wall"))
                && hit.normal.y > 0.95f)
            {
                return true;
            }

            return false;
        }
    }

    void MoveTowards(
        Vector3 targetPos, Rigidbody rb, float targetVel, float maxVel)
    {
        var moveToPos = targetPos - rb.worldCenterOfMass;
        var velocityTarget = Time.fixedDeltaTime * targetVel * moveToPos;
        if (float.IsNaN(velocityTarget.x) == false)
        {
            rb.velocity = Vector3.MoveTowards(
                rb.velocity, velocityTarget, maxVel);
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        var agentPos = m_AgentRb.position - ground.transform.position;

        sensor.AddObservation(agentPos / 20f);
        sensor.AddObservation(DoGroundCheck(true) ? 1 : 0);
    }

    public void MoveAgent(ActionSegment<int> act)
    {
        AddReward(-0.0005f);
        var smallGrounded = DoGroundCheck(true);
        var largeGrounded = DoGroundCheck(false);

        // small reward for height above 4.0f
        var height = transform.position.y - ground.transform.position.y;
        if (height > 4.0f)
        {
            AddReward(0.001f * height);
        }

        // reward depending how close agent is to the goal
        var distanceToGoal = Vector3.Distance(
            transform.localPosition, goal.transform.localPosition);
        AddReward(-0.0001f * distanceToGoal);

        // reward when visiting new squares, punish for being in visited, bounds are -10 to 10, -14 to 6
        var x = (int)transform.localPosition.x + 10;
        var z = (int)transform.localPosition.z + 14;
        if (m_VisitedStates[x, z] != 1)
        {
            m_VisitedStates[x, z] = 1;
            AddReward(0.5f);
        } else {
            AddReward(-0.005f);
        }

        var dirToGo = Vector3.zero;
        var rotateDir = Vector3.zero;
        var dirToGoForwardAction = act[0];
        var rotateDirAction = act[1];
        var dirToGoSideAction = act[2];
        var jumpAction = act[3];

        if (dirToGoForwardAction == 1)
            dirToGo = (largeGrounded ? 1f : 0.5f) * 1f * transform.forward;
        else if (dirToGoForwardAction == 2)
            dirToGo = (largeGrounded ? 1f : 0.5f) * -1f * transform.forward;
        if (rotateDirAction == 1)
            rotateDir = transform.up * -1f;
        else if (rotateDirAction == 2)
            rotateDir = transform.up * 1f;
        if (dirToGoSideAction == 1)
            dirToGo = (largeGrounded ? 1f : 0.5f) * -0.6f * transform.right;
        else if (dirToGoSideAction == 2)
            dirToGo = (largeGrounded ? 1f : 0.5f) * 0.6f * transform.right;
        if (jumpAction == 1)
            if ((jumpingTime <= 0f) && smallGrounded)
            {
                Jump();
            }

        transform.Rotate(rotateDir, Time.fixedDeltaTime * 300f);
        m_AgentRb.AddForce(dirToGo * m_WallJumpSettings.agentRunSpeed,
            ForceMode.VelocityChange);

        if (jumpingTime > 0f)
        {
            m_JumpTargetPos =
                new Vector3(m_AgentRb.position.x,
                    m_JumpStartingPos.y + m_WallJumpSettings.agentJumpHeight,
                    m_AgentRb.position.z) + dirToGo;
            MoveTowards(m_JumpTargetPos, m_AgentRb, m_WallJumpSettings.agentJumpVelocity,
                m_WallJumpSettings.agentJumpVelocityMaxChange);
        }

        if (!(jumpingTime > 0f) && !largeGrounded)
        {
            m_AgentRb.AddForce(
                Vector3.down * fallingForce, ForceMode.Acceleration);
        }
        jumpingTime -= Time.fixedDeltaTime;
    }

    public override void OnActionReceived(ActionBuffers actionBuffers)

    {
        MoveAgent(actionBuffers.DiscreteActions);
        if ((!Physics.Raycast(m_AgentRb.position, Vector3.down, 20)))
            // || (!Physics.Raycast(m_ShortBlockRb.position, Vector3.down, 20)))
        {
            SetReward(-1f);
            EndEpisode();
            // ResetBlock(m_ShortBlockRb);
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActionsOut = actionsOut.DiscreteActions;
        if (Input.GetKey(KeyCode.D))
        {
            discreteActionsOut[1] = 2;
        }
        if (Input.GetKey(KeyCode.W))
        {
            discreteActionsOut[0] = 1;
        }
        if (Input.GetKey(KeyCode.A))
        {
            discreteActionsOut[1] = 1;
        }
        if (Input.GetKey(KeyCode.S))
        {
            discreteActionsOut[0] = 2;
        }
        discreteActionsOut[3] = Input.GetKey(KeyCode.Space) ? 1 : 0;
    }

    // Detect when the agent hits the goal
    void OnTriggerStay(Collider col)
    {
        if (col.gameObject.CompareTag("goal")) 
            // && DoGroundCheck(true))
        {
            SetReward(1.0f);
            EndEpisode();
        }
    }

    public override void OnEpisodeBegin()
    {
        transform.localPosition = new Vector3(
            -2, 1, -12);
        m_AgentRb.velocity = Vector3.zero;
        m_AgentRb.angularVelocity = Vector3.zero;

        m_VisitedStates = new int[25, 25];
        m_TimeoutStartTime = Time.time;
    }



    void FixedUpdate()
    {
        if (Time.time > m_TimeoutStartTime + m_EnvironmentTimeout)
        {
            AddReward(-5.0f);
            m_TimeoutStartTime = Time.time;
            EndEpisode();
        }
    }
}
