using Sandbox.Game.Weapons;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRageMath;

namespace ToolCore
{
    internal class TestGun : IMyGunObject<MyToolBase>
    {
        private ToolComp _comp;
        private MyDefinitionId _id;
        public float BackkickForcePerSecond
        {
            get { return 0f; }
        }

        public float ShakeAmount
        {
            get { return 0f; }
        }

        public MyDefinitionId DefinitionId
        {
            get { return _id; }
        }

        public bool EnabledInWorldRules
        {
            get { return true; }
        }

        public MyToolBase GunBase
        {
            get { return null; }
        }

        public bool IsSkinnable
        {
            get { return false; }
        }

        public bool IsTargetLockingCapable
        {
            get { return false; }
        }

        public bool IsShooting
        {
            //get { return (uint)_comp.State > 1; }
            get { return false; }
        }

        public int ShootDirectionUpdateTime
        {
            get { return 0; }
        }

        public bool NeedsShootDirectionWhileAiming
        {
            get { return false; }
        }

        public float MaximumShotLength
        {
            get { return 0f; }
        }

        public void BeginFailReaction(MyShootActionEnum action, MyGunStatusEnum status)
        {
        }

        public void BeginFailReactionLocal(MyShootActionEnum action, MyGunStatusEnum status)
        {
        }

        public void BeginShoot(MyShootActionEnum action)
        {
        }

        public bool CanShoot(MyShootActionEnum action, long shooter, out MyGunStatusEnum status)
        {
            status = MyGunStatusEnum.OK;
            //if (action != MyShootActionEnum.PrimaryAction)
            //{
            //    status = MyGunStatusEnum.Failed;
            //    return false;
            //}
            //if (!_comp.Functional)
            //{
            //    status = MyGunStatusEnum.NotFunctional;
            //    return false;
            //}
            //if (!base.HasPlayerAccess(shooter, MyRelationsBetweenPlayerAndBlock.NoOwnership))
            //{
            //    status = MyGunStatusEnum.AccessDenied;
            //    return false;
            //}
            //if (MySandboxGame.TotalGamePlayTimeInMilliseconds - this.m_lastTimeActivate < 250)
            //{
            //    status = MyGunStatusEnum.Cooldown;
            //    return false;
            //}
            return true;
        }

        public Vector3 DirectionToTarget(Vector3D target)
        {
            return Vector3.Zero;
        }

        public void DrawHud(IMyCameraController camera, long playerId)
        {
        }

        public void DrawHud(IMyCameraController camera, long playerId, bool fullUpdate)
        {
        }

        public void EndShoot(MyShootActionEnum action)
        {
            if (action != MyShootActionEnum.PrimaryAction)
            {
                return;
            }
            //if (!this.Enabled)
            //{
            //    this.StopShooting();
            //}
        }

        public int GetAmmunitionAmount()
        {
            return 0;
        }

        public int GetMagazineAmount()
        {
            return 0;
        }

        public Vector3D GetMuzzlePosition()
        {
            //return _comp.Muzzle.Matrix.Translation;
            return Vector3D.Zero;
        }

        public Vector3 GetShootDirection()
        {
            //return _comp.Muzzle.Matrix.Forward;
            return Vector3.Zero;
        }

        public int GetTotalAmmunitionAmount()
        {
            return 0;
        }

        public bool IsToolbarUsable()
        {
            return true;
        }

        public void OnControlAcquired(IMyCharacter owner)
        {
            if (owner == null || owner.Parent == null)
            {
                return;
            }
            //if (owner == MySession.Static.LocalCharacter && !owner.Parent.Components.Contains(typeof(MyCasterComponent)))
            //{
            //    MyCasterComponent component = new MyCasterComponent(new MyDrillSensorRayCast(0f, this.DEFAULT_REACH_DISTANCE, base.BlockDefinition));
            //    owner.Parent.Components.Add<MyCasterComponent>(component);
            //    this.m_controller = (MyCharacter)owner;
            //}
        }

        public void OnControlReleased()
        {

        }

        public void Shoot(MyShootActionEnum action, Vector3 direction, Vector3D? overrideWeaponPos, string gunAction = null)
        {
            //Do Stuff
        }

        public void ShootFailReactionLocal(MyShootActionEnum action, MyGunStatusEnum status)
        {
        }

        public bool SupressShootAnimation()
        {
            return false;
        }

        public void UpdateSoundEmitter()
        {
        }
    }
}
