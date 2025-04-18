using UnityEngine;
using UnityEngine.AI;

public class NavMeshWander : MonoBehaviour {
  public float roamRadius = 20f;
  public float wanderInterval = 5f;
  NavMeshAgent agent;

  void Awake() {
    agent = GetComponent<NavMeshAgent>();
  }

  void Start() {
    InvokeRepeating(nameof(PickNewDestination), 0, wanderInterval);
  }

  void PickNewDestination() {
    // pick random point in circle
    Vector3 rnd = transform.position + Random.insideUnitSphere * roamRadius;
    NavMeshHit hit;
    // sample to nearest NavMesh point
    if (NavMesh.SamplePosition(rnd, out hit, roamRadius, NavMesh.AllAreas)) {
      agent.SetDestination(hit.position);
    }
  }
}
