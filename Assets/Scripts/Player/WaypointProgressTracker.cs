using System;
using UnityEngine;
using UnityEngine.UI;

#pragma warning disable 649
namespace UnityStandardAssets.Utility
{
    public class WaypointProgressTracker : MonoBehaviour
    {
        #region Race/Time Trial Event Variables
        [HideInInspector] public int currentLap = 1;
        [HideInInspector] public int numberOfLaps = 0;

        [HideInInspector] public bool halfPointOftrackReached = false;
        [HideInInspector] public int lastIndex;

        [HideInInspector] public Race currentRace;
        [HideInInspector] public TimeTrial currentTimeTrial;

        [HideInInspector] public float timeOfCompletion;
        [HideInInspector] public Route.RouteType routeType; //Here simply to dictate type of route

        private PlayerController player;
        private TimeSpan eventDuration;
        #endregion

        // This script can be used with any object that is supposed to follow a
        // route marked out by waypoints.

        // This script manages the amount to look ahead along the route,
        // and keeps track of progress and laps.

        [SerializeField] public WaypointCircuit Circuit; // A reference to the waypoint-based route we should follow

        [SerializeField] public WaypointCircuit[] Routes;

        [SerializeField] public int straightRouteTargetPoint;

        [SerializeField] private float lookAheadForTargetOffset = 5f;
        // The offset ahead along the route that the we will aim for

        [SerializeField] private float lookAheadForTargetFactor = .1f;
        // A multiplier adding distance ahead along the route to aim for, based on current speed

        [SerializeField] private float lookAheadForSpeedOffset = 10;
        // The offset ahead only the route for speed adjustments (applied as the rotation of the waypoint target transform)

        [SerializeField] private float lookAheadForSpeedFactor = .2f;
        // A multiplier adding distance ahead along the route for speed adjustments

        [SerializeField] public ProgressStyle progressStyle = ProgressStyle.SmoothAlongRoute;
        // whether to update the position smoothly along the route (good for curved paths) or just when we reach each waypoint.

        [SerializeField] private float pointToPointThreshold = 4;
        // proximity to waypoint which must be reached to switch target to next waypoint : only used in PointToPoint mode.

        [SerializeField] Transform target;

        public enum ProgressStyle
        {
            SmoothAlongRoute,
            PointToPoint,
        }

        // these are public, readable by other objects - i.e. for an AI to know where to head!
        public WaypointCircuit.RoutePoint targetPoint { get; private set; }
        public WaypointCircuit.RoutePoint speedPoint { get; private set; }
        public WaypointCircuit.RoutePoint progressPoint { get; private set; }

        [HideInInspector] public float progressDistance; // The progress around the route, used in smooth mode.

        private int progressNum; // the current waypoint number, used in point-to-point mode.
        private Vector3 lastPosition; // used to calculate current speed (since we may not have a rigidbody component)
        private float speed; // current speed of this object (calculated from delta since last frame)

        private void Awake()
        {
            Routes = FindObjectsOfType<WaypointCircuit>();
            Circuit = Routes[0];
        }

        // setup script properties
        private void Start()
        {
            player = GetComponent<PlayerController>();

            lastPosition = transform.position;

            // we use a transform to represent the point to aim for, and the point which
            // is considered for upcoming changes-of-speed. This allows this component
            // to communicate this information to the AI without requiring further dependencies.

            // you can manually create a transform and assign it to this component *and* the AI,
            // then this component will update it, and the AI can read it.
            if (target == null)
            {
                target = new GameObject(name + " Waypoint Target").transform;
            }

            Reset();
        }

        private void FixedUpdate()
        {
            CheckIfLapComplete();

            if (player.Paused()) return;

            if (Circuit == null) return;
            
            if (Circuit.GetRoutePoint(0).Equals(null)) return;

            lookAheadForTargetOffset = player.GetVelocity();

            if (progressStyle == ProgressStyle.SmoothAlongRoute)
            {                   
                // determine the position we should currently be aiming for
                // (this is different to the current progress position, it is a a certain amount ahead along the route)
                // we use lerp as a simple way of smoothing out the speed over time.
                if (Time.fixedDeltaTime > 0)
                {
                    UpdatePosition();
                }

                if (routeType == Route.RouteType.LoopedTrack && halfPointOftrackReached == false)
                {
                    if (CheckIfHalfPointOfTrackReached() == true)
                        halfPointOftrackReached = true;
                }
            }
            else
            {
                // point to point mode. Just increase the waypoint if we're close enough:
                Vector3 targetDelta = target.position - transform.position;
                if (targetDelta.magnitude < pointToPointThreshold)
                {
                    progressNum = (progressNum + 1) % Circuit.Waypoints.Length;
                    UpdateEventLapCount();
                }

                target.position = Circuit.Waypoints[progressNum].position;
                target.rotation = Circuit.Waypoints[progressNum].rotation;

                // get our current progress along the route
                progressPoint = Circuit.GetRoutePoint(progressDistance);
                Vector3 progressDelta = progressPoint.position - transform.position;
                if (Vector3.Dot(progressDelta, progressPoint.direction) < 0)
                {
                    progressDistance += progressDelta.magnitude;
                }
                lastPosition = transform.position;
            }
        }

        public void Reset()
        {
            progressDistance = 0;
            progressNum = 0;
            //straightRouteTargetPoint = 1;

            if (progressStyle == ProgressStyle.PointToPoint)
            {
                target.position = Circuit.Waypoints[progressNum].position;
                target.rotation = Circuit.Waypoints[progressNum].rotation;
            }
        }

        public void SetCircuit(WaypointCircuit waypointCircuit)
        {
            Circuit = waypointCircuit;
        }

        public void UpdateLastNodeIndex(int lastNodeIndex)
        {
            lastIndex = lastNodeIndex;
            straightRouteTargetPoint = lastNodeIndex;
        }

        public void SetRace(Race race)
        {
            currentRace = race;
        }

        public void UpdateLaps(int numberOfLaps)
        {
            this.numberOfLaps = numberOfLaps;
        }

        public void UpdatePosition()
        {
            speed = Mathf.Lerp(speed, (lastPosition - transform.position).magnitude / Time.fixedDeltaTime, Time.fixedDeltaTime);

            target.position =
                Circuit.GetRoutePoint(progressDistance + lookAheadForTargetOffset + lookAheadForTargetFactor * speed)
                        .position;

            
            target.rotation =
                Quaternion.LookRotation(
                    Circuit.GetRoutePoint(progressDistance + lookAheadForSpeedOffset + lookAheadForSpeedFactor * 0.2f)
                            .direction);
            

            // get our current progress along the route
            progressPoint = Circuit.GetRoutePoint(progressDistance);
            Vector3 progressDelta = progressPoint.position - transform.position;
            if (Vector3.Dot(progressDelta, progressPoint.direction) < 0)
            {
                progressDistance += progressDelta.magnitude * 0.5f;
            }

            lastPosition = transform.position;
        }

        private bool CheckIfHalfPointOfTrackReached()
        {
            if (!player.participatingInRace && !player.participatingInTimeTrial) return false;

            float distance;
            int middleIndex = lastIndex / 2;
            if (target.GetComponent<PlayerController>().participatingInRace == true)
            {
                distance = Vector3.Distance(transform.position, currentRace.track[middleIndex].position);

                if (distance < pointToPointThreshold)
                {
                    return true;
                }
            }
            else if (target.GetComponent<PlayerController>().participatingInTimeTrial == true)
            {
                distance = Vector3.Distance(transform.position, currentTimeTrial.track[middleIndex].position);

                if (distance < pointToPointThreshold)   
                {
                    return true;
                }
            }

            return false;
        }

        private void CheckIfLapComplete()
        {
            if(routeType == Route.RouteType.LinearTrack)
            {
                CheckIfStraightRouteLapComplete();
            }
            else if(routeType == Route.RouteType.LoopedTrack)
            {
                CheckLoopedRouteLapComplete();
            }
        }

        private void CheckIfStraightRouteLapComplete()
        {
            if (target.GetComponent<PlayerController>().participatingInRace == false && target.GetComponent<PlayerController>().participatingInRace == false) return;

            float distance;
            if (target.GetComponent<PlayerController>().participatingInRace == true)
            {
                distance = Vector3.Distance(transform.position, currentRace.track[straightRouteTargetPoint].position);
            }
            else
            {
                distance = Vector3.Distance(transform.position, currentTimeTrial.track[straightRouteTargetPoint].position);
            }

            if (distance < pointToPointThreshold)
            {               
                UpdateEventLapCount();                
            }
        }

        private void CheckLoopedRouteLapComplete()
        {
            // Precaution to avoid this firing off instantly on start of race
            if (halfPointOftrackReached == true)
            {
                float distance;
                if (target.GetComponent<PlayerController>().participatingInRace == true)
                {
                    distance = Vector3.Distance(transform.position, currentRace.track[lastIndex].position);
                }
                else
                {
                    distance = Vector3.Distance(transform.position, currentTimeTrial.track[lastIndex].position);
                }

                if (distance < pointToPointThreshold)
                {
                    halfPointOftrackReached = false;
                    UpdateEventLapCount();
                }
            }
        }

        private void UpdateEventLapCount()
        {
            if (currentLap < numberOfLaps)
            {
                UpdateStraightRouteTarget();
                currentLap++;
                RotatePlayerToRepeatLinearTrack();
            }
            else
            {
                speed = 0;
                CompleteEvent();
                ResetEventData();
            }
        }

        private void UpdateStraightRouteTarget()
        {
            if(straightRouteTargetPoint == 0)
            {
                straightRouteTargetPoint = 1;
            }
            else
            {
                straightRouteTargetPoint = 0;
            }
        }

        //Rotate the player 180 degrees instantly to allow them to travel back the other way of the linear track
        private void RotatePlayerToRepeatLinearTrack()
        {
            target.transform.RotateAround(target.transform.position, target.transform.up, 180f);
            //player.ChangeCameraView();
            //target.transform.Rotate(0, Time.deltaTime*30,0,Space.Self);
        }

        private void CompleteEvent()
        {
            if (target.GetComponent<PlayerController>().participatingInRace == true)
            {
                // Time, position and race info(amount of laps, track(distance), amount of participants) to be all stored relating to the new highscore obtained for this particular setup->Add with Player data
                eventDuration = TimeSpan.FromSeconds(Time.timeSinceLevelLoad - currentRace.timeRaceStarted);
                //currentRace.AddParticipantToCompletedRaceList(target.GetComponent<PlayerController>());
                currentRace.AddParticipantToCompletedRaceList(player);
            }
            else if (target.GetComponent<PlayerController>().participatingInTimeTrial == true)
            {
                currentTimeTrial.timeTrialComplete = true;
            }
        }

        private void OnDrawGizmos()
        {
            if (Application.isPlaying)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, target.position);
                Gizmos.DrawWireSphere(Circuit.GetRoutePosition(progressDistance), 1);
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(target.position, target.position + target.forward);
            }
        }

        private void ResetEventData()
        {
            progressDistance = 0;
            progressNum = 0;
            halfPointOftrackReached = false;
            lastIndex = 0;
            currentRace = null;
            currentTimeTrial = null;
            timeOfCompletion = 0;
            numberOfLaps = 0;
            currentLap = 1;
            straightRouteTargetPoint = 1;
        }
    }
}
