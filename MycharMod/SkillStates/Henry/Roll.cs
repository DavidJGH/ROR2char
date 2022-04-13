using System.Linq;
using System.Threading.Tasks;
using EntityStates;
using EntityStates.Commando;
using MycharMod.Modules;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace MycharMod.SkillStates
{
    public class Roll : BaseSkillState
    {
        public static float duration = 0.3f;
        public static float initialSpeedCoefficient = 5f;
        public static float finalSpeedCoefficient = 2.5f;

        public static string dodgeSoundString = "HenryRoll";
        public static float dodgeFOV = DodgeState.dodgeFOV;
        private Animator animator;
        private Vector3 forwardDirection;
        private Vector3 previousPosition;

        private float rollSpeed;

        public override void OnEnter()
        {
            base.OnEnter();
            animator = GetModelAnimator();

            if (isAuthority && inputBank) forwardDirection = inputBank.aimDirection.normalized;

            characterMotor.Motor.SetMovementCollisionsSolvingActivation(false);
            characterMotor.Motor.SetGroundSolvingActivation(false);

            RecalculateRollSpeed();

            if (characterMotor && characterDirection) characterMotor.velocity = forwardDirection * rollSpeed;

            Vector3 b = characterMotor ? characterMotor.velocity : Vector3.zero;
            previousPosition = transform.position - b;

            PlayAnimation("FullBody, Override", "Roll", "Roll.playbackRate", duration);
            Util.PlaySound(dodgeSoundString, gameObject);

            if (NetworkServer.active)
            {
                characterBody.AddTimedBuff(Buffs.armorBuff, 3f * duration);
                characterBody.AddTimedBuff(RoR2Content.Buffs.HiddenInvincibility, 0.5f * duration);
            }
        }

        private void OnCollision(ref CharacterMotor.MovementHitInfo info)
        {
            Log.Message(info.velocity);
        }

        private void RecalculateRollSpeed()
        {
            rollSpeed = moveSpeedStat * Mathf.Lerp(initialSpeedCoefficient, finalSpeedCoefficient, fixedAge / duration);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            RecalculateRollSpeed();

            if (characterDirection) characterDirection.forward = forwardDirection;
            if (cameraTargetParams) cameraTargetParams.fovOverride = Mathf.Lerp(dodgeFOV, 60f, fixedAge / duration);

            Vector3 normalized = (transform.position - previousPosition).normalized;
            if (characterMotor && characterDirection && normalized != Vector3.zero)
            {
                Vector3 vector = normalized * rollSpeed;
                float d = Mathf.Max(Vector3.Dot(vector, forwardDirection), 0f);
                vector = forwardDirection * d;

                characterMotor.velocity = vector;
            }

            previousPosition = transform.position;

            if (isAuthority && fixedAge >= duration) outer.SetNextStateToMain();
        }

        public override void OnExit()
        {
            if (cameraTargetParams) cameraTargetParams.fovOverride = -1f;
            base.OnExit();

            WaitForItToWork().ContinueWith(task => task);

            characterMotor.disableAirControlUntilCollision = false;
        }

        public override void OnSerialize(NetworkWriter writer)
        {
            base.OnSerialize(writer);
            writer.Write(forwardDirection);
        }

        public override void OnDeserialize(NetworkReader reader)
        {
            base.OnDeserialize(reader);
            forwardDirection = reader.ReadVector3();
        }

        private async Task<bool> WaitForItToWork()
        {
            bool succeeded = false;
            while (!succeeded)
            {
                float length = 20f;
                RaycastHit? hitUp = GetClosestUndergroundHit(length, Vector3.up);
                RaycastHit? hitDown = GetClosestUndergroundHit(length, Vector3.down);
                RaycastHit? hitLeft = GetClosestUndergroundHit(length, Vector3.left);
                RaycastHit? hitRight = GetClosestUndergroundHit(length, Vector3.right);
                RaycastHit? hitForward = GetClosestUndergroundHit(length, Vector3.forward);
                RaycastHit? hitBack = GetClosestUndergroundHit(length, Vector3.back);

                RaycastHit?[] hits = { hitUp, hitDown, hitLeft, hitRight, hitForward, hitBack };

                Vector3 velocity = characterMotor.velocity;

                var finalHit = hits.Where((hit) => hit.HasValue)
                    .OrderBy((hit) =>
                    {
                        var alignment = Vector3.Dot((hit.Value.point - transform.position).normalized,
                            velocity.normalized);
                        return (length - hit.Value.distance) / (1 + (alignment / 2f));
                    })
                    .FirstOrDefault();

                if (finalHit.HasValue)
                {
                    characterMotor.velocity += moveSpeedStat * (finalHit.Value.point - transform.position).normalized *
                                               Mathf.Clamp(length - finalHit.Value.distance, 3f, 4.5f) * 0.2f;
                }
                else
                {
                    succeeded = true;
                }

                await Task.Delay(100);
            }

            characterMotor.Motor.SetMovementCollisionsSolvingActivation(true);
            characterMotor.Motor.SetGroundSolvingActivation(true);

            return succeeded;
        }

        private RaycastHit? GetClosestUndergroundHit(float length, Vector3 direction)
        {
            ShootRaycastsToAndFrom(length, direction, out RaycastHit[] hits, out RaycastHit[] hitsR);
            return GetClosestUndergroundHit(hits, hitsR, length);
        }

        private void ShootRaycastsToAndFrom(float length, Vector3 direction, out RaycastHit[] hitsUp,
            out RaycastHit[] hitsUpR)
        {
            var position = transform.position;
            hitsUp =
                Physics.RaycastAll(position + direction * length, -direction, length, ~0,
                        QueryTriggerInteraction.Ignore)
                    .Where(hit => hit.transform.name != "MainHurtbox" && !hit.transform.name.Contains("HenryBody"))
                    .ToArray();
            hitsUpR = Physics.RaycastAll(position, direction, length, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => hit.transform.name != "MainHurtbox" && !hit.transform.name.Contains("HenryBody"))
                .ToArray();
        }

        private static RaycastHit? GetClosestUndergroundHit(RaycastHit[] hits, RaycastHit[] hitsR, float length)
        {
            return hits.OrderBy((hit) =>
                    length - hit.distance)
                .Select(hit => (RaycastHit?)hit)
                .FirstOrDefault(hit => !hitsR.Any(
                    (hitR) => hitR.transform.Equals(hit.Value.transform) &&
                              hitR.distance < length - hit.Value.distance));
        }
    }
}