using Sandbox.Game.EntityComponents;
using Sandbox.Game.Weapons;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ToolCore.Definitions.Serialised;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRageMath;
using ToolCore.Utils;
using Sandbox.Game.Entities;
using VRage.Game.ObjectBuilders.Components;
using static ToolCore.Utils.Utils;
using Sandbox.ModAPI;

namespace ToolCore.Comp
{
    internal class CoreGun : IMyGunObject<MyToolBase>
    {
        public CoreGun(ToolComp comp)
        {
            _comp = comp;
            _id = comp.Tool.BlockDefinition;
        }

        private ToolComp _comp;
        private MyDefinitionId _id;

        internal bool WantsToShoot;
        internal bool Primary = true;
        internal bool Shooting;
        internal bool Enabled;

        internal ToolComp.ToolAction GunAction;

        internal enum ShootState
        {
            None = 0,
            Primary = 1,
            Secondary = 2,
            Tertiary = 3,
        }

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
            get { return MyAPIGateway.Session.WeaponsEnabled; }
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
            get { return Shooting; }
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
            //Logs.WriteLine($"BeginShoot : {action}");
        }

        public bool CanShoot(MyShootActionEnum action, long shooter, out MyGunStatusEnum status)
        {
            status = MyGunStatusEnum.OK;
            //if (action != MyShootActionEnum.PrimaryAction)
            //{
            //    status = MyGunStatusEnum.Failed;
            //    return false;
            //}
            if (!_comp.Functional)
            {
                status = MyGunStatusEnum.NotFunctional;
                return false;
            }
            if (!_comp.Enabled)
            {
                status = MyGunStatusEnum.Disabled;
                return false;
            }
            if (!_comp.Powered)
            {
                status = MyGunStatusEnum.OutOfPower;
                return false;
            }
            if (!_comp.Tool.HasPlayerAccess(shooter, MyRelationsBetweenPlayerAndBlock.NoOwnership))
            {
                status = MyGunStatusEnum.AccessDenied;
                return false;
            }
            if (!MySessionComponentSafeZones.IsActionAllowed(_comp.Grid, CastHax(MySessionComponentSafeZones.AllowedActions, (int)_comp.Mode), shooter))
            {
                status = MyGunStatusEnum.Failed;
                return false;
            }
            //if (MySandboxGame.TotalGamePlayTimeInMilliseconds - this.m_lastTimeActivate < 250)
            //{
            //    status = MyGunStatusEnum.Cooldown;
            //    return false;
            //}
            return true;
        }

        public Vector3 DirectionToTarget(Vector3D target)
        {
            return _comp.Muzzle.Matrix.Forward;
        }

        public void DrawHud(IMyCameraController camera, long playerId)
        {
        }

        public void DrawHud(IMyCameraController camera, long playerId, bool fullUpdate)
        {
        }

        public void EndShoot(MyShootActionEnum action)
        {
            //Logs.WriteLine($"EndShoot : {action}");
            WantsToShoot = false;

            if (_comp.Activated || !Shooting)
                return;

            var state = action == MyShootActionEnum.PrimaryAction ? Trigger.LeftClick : Trigger.RightClick;
            UpdateShootState(state);

            //Logs.WriteLine("read: " + _comp.Session.DsUtil.GetValue("read").ToString());
            //Logs.WriteLine("sort: " + _comp.Session.DsUtil.GetValue("sort").ToString());
            //Logs.WriteLine("calc: " + _comp.Session.DsUtil.GetValue("calc").ToString());
            //Logs.WriteLine("write: " + _comp.Session.DsUtil.GetValue("write").ToString());
            //Logs.WriteLine("notify: " + _comp.Session.DsUtil.GetValue("notify").ToString());
            //_comp.Session.DsUtil.Clean();
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
            return _comp.Muzzle.Matrix.Translation;
        }

        public Vector3 GetShootDirection()
        {
            return _comp.Muzzle.Matrix.Forward;
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
            //Logs.WriteLine($"Shoot : {action}");
            if (!WantsToShoot)
            {
                _comp.Sink.Update();
                _comp.UpdatePower = true;
            }

            WantsToShoot = true;
            GunAction = (ToolComp.ToolAction)action;

            var happy = _comp.Functional && _comp.Powered && _comp.Enabled && !_comp.Activated;
            if (Shooting == happy)
                return;

            var state = action == MyShootActionEnum.PrimaryAction ? Trigger.LeftClick : Trigger.RightClick;
            UpdateShootState(state);
        }

        public void ShootFailReactionLocal(MyShootActionEnum action, MyGunStatusEnum status)
        {
            Logs.WriteLine("ShootFailReactionLocal");
        }

        public bool SupressShootAnimation()
        {
            return false;
        }

        internal void UpdateShootState(Trigger state)
        {
            Shooting = WantsToShoot && _comp.Functional && _comp.Powered && _comp.Enabled && !_comp.Activated;
            Logs.WriteLine($"Setting Shooting to {Shooting}");

            _comp.UpdateState(state, Shooting);
        }

        public void UpdateSoundEmitter()
        {
        }
    }
}
