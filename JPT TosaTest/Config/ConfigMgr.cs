﻿using CJPT_TosaTestPAS.Config.SoftwareManager;
using GalaSoft.MvvmLight.Messaging;
using JPT_TosaTest.Classes;
using JPT_TosaTest.Config.HardwareManager;
using JPT_TosaTest.Config.SoftwareManager;
using JPT_TosaTest.Config.SystemCfgManager;
using JPT_TosaTest.Config.UserManager;
using JPT_TosaTest.Instrument;
using JPT_TosaTest.IOCards;
using JPT_TosaTest.MotionCards;
using JPT_TosaTest.WorkFlow;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace JPT_TosaTest.Config
{
    public enum EnumConfigType
    {
        HardwareCfg,
        SoftwareCfg,
        SystemParaCfg,
        UserCfg,
    }
    public class ConfigMgr
    {
        private ConfigMgr()
        {

        }
        private static readonly Lazy<ConfigMgr> _instance = new Lazy<ConfigMgr>(() => new ConfigMgr());
        public static ConfigMgr Instance
        {
            get { return _instance.Value; }
        }
        private readonly string File_HardwareCfg = FileHelper.GetCurFilePathString() + "Config\\HardwareCfg.json";
        private readonly string File_SoftwareCfg = FileHelper.GetCurFilePathString() + "Config\\SoftwareCfg.json";
        private readonly string File_SystemParaCfg = FileHelper.GetCurFilePathString() + "Config\\SystemParaCfg.json";
        private readonly string File_UserCfg = FileHelper.GetCurFilePathString() + "User.json";

        public  HardwareCfgManager HardwareCfgMgr = null;
        public  SoftwareCfgManager SoftwareCfgMgr = null;
        public  SystemParaCfgManager SystemParaCfgMgr =null;
        public UserCfgManager UserCfgMgr = null;


        //public static 
        public void LoadConfig(out List<string> errList)
        {
            #region >>>>Hardware init
            errList = new List<string>();
            try
            {
                var json_string = File.ReadAllText(File_HardwareCfg);
                HardwareCfgMgr = JsonConvert.DeserializeObject<HardwareCfgManager>(json_string);
            }
            catch (Exception ex)
            {
                errList.Add($"Unable to load config file { File_HardwareCfg}, { ex.Message}");
            }

            MotionBase motionBase = null;
            IOBase ioBase = null;
            InstrumentBase instrumentBase = null;
            Type hardWareMgrType = HardwareCfgMgr.GetType();
            foreach (var it in hardWareMgrType.GetProperties())
            {
                switch (it.Name)
                {
                    case "MotionCards":
                        var motionCfgs = it.GetValue(HardwareCfgMgr) as MotionCardCfg[];
                        if (motionCfgs == null)
                            break;
                        foreach (var motionCfg in motionCfgs)
                        {
                            if (motionCfg.Enabled)
                            {
                                motionBase = hardWareMgrType.Assembly.CreateInstance("JPT_TosaTest.MotionCards." + motionCfg.Name.Substring(0, motionCfg.Name.Length - 3), true, BindingFlags.CreateInstance, null, /*new object[] { motionCfg }*/null, null, null) as MotionBase;
                                if (motionBase != null)
                                {
                                    if (motionCfg.NeedInit)
                                    {
                                        if (motionBase.Init(motionCfg))
                                            MotionMgr.Instance.AddMotionCard(motionCfg.Name, motionBase);
                                        else
                                            errList.Add($"{motionCfg.Name} init failed");
                                    }
                                }
                                else
                                {
                                    errList.Add($"{motionCfg.Name} Create instanse failed");
                                }
                            }
                        }
                        break;
                    case "IOCards":
                        var ioCfgs = it.GetValue(HardwareCfgMgr) as IOCardCfg[];
                        if (ioCfgs == null)
                            break;
                        foreach (var ioCfg in ioCfgs)
                        {
                            if (ioCfg.Enabled)
                            {
                                ioBase = hardWareMgrType.Assembly.CreateInstance("JPT_TosaTest.IOCards." + ioCfg.Name.Substring(0, ioCfg.Name.Length - 3), true, BindingFlags.CreateInstance, null, null, null, null) as IOBase;
                                if (ioBase != null)
                                {
                                    if (ioCfg.NeedInit)
                                    {
                                        if (ioBase.Init(ioCfg))
                                            IOCardMgr.Instance.AddIOCard(ioCfg.Name, ioBase);
                                        else
                                            errList.Add($"{ioCfg.Name} init failed");
                                    }
                                }
                                else
                                {
                                    errList.Add($"{ioCfg.Name} Create instanse failed");
                                }
                            }
                        }
                        break;
                    case "Instruments":
                        var instrumentCfgs = it.GetValue(HardwareCfgMgr) as InstrumentCfg[];
                        if (instrumentCfgs == null)
                            break;
                        foreach (var instrumentCfg in instrumentCfgs)
                        {
                            if (instrumentCfg.Enabled)
                            {
                                instrumentBase = hardWareMgrType.Assembly.CreateInstance("JPT_TosaTest.IOCards." + instrumentCfg.InstrumentName.Substring(0, instrumentCfg.InstrumentName.Length - 3), true, BindingFlags.CreateInstance, null, null, null, null) as InstrumentBase;
                            }
                        }
                        break;
                    case "Cameras":
                        var cameraCfgs = it.GetValue(HardwareCfgMgr) as CameraCfg[];
                        break;
                    case "Comports":
                    case "EtherNets":
                    case "GPIBs":
                    case "NIVisas":
                        break;
                    default:
                        errList.Add("Invalid hardwaer type!");
                        break;

                }
            }

            #endregion

            #region >>>>Software init
            try
            {
                var json_string = File.ReadAllText(File_SoftwareCfg);
                SoftwareCfgMgr = JsonConvert.DeserializeObject<SoftwareCfgManager>(json_string);
            }
            catch (Exception ex)
            {
                Messenger.Default.Send<string>(String.Format("Unable to load config file {0}, {1}", File_SoftwareCfg, ex.Message), "ShowError");
                throw new Exception(ex.Message);
            }

            Type tStationCfg = SoftwareCfgMgr.GetType();
            PropertyInfo[] pis = tStationCfg.GetProperties();
            WorkFlowConfig[] WorkFlowCfgs = null;
            WorkFlowBase workFlowBase = null;
            foreach (PropertyInfo pi in pis)
            {
                WorkFlowCfgs = pi.GetValue(SoftwareCfgMgr) as SoftwareManager.WorkFlowConfig[];
                foreach (var it in WorkFlowCfgs)
                {
                    if (it.Enable)
                    {
                        workFlowBase = tStationCfg.Assembly.CreateInstance("CPAS.WorkFlow." + it.Name, true, BindingFlags.CreateInstance, null, new object[] { it }, null, null) as WorkFlowBase;
                        WorkFlowMgr.Instance.AddStation(it.Name, workFlowBase);
                    }
                }
            }
            #endregion

            #region >>>>SystemCfg
            try
            {
                var json_string = File.ReadAllText(File_SystemParaCfg);
                SystemParaCfgMgr = JsonConvert.DeserializeObject<SystemParaCfgManager>(json_string);
            }
            catch (Exception ex)
            {
                Messenger.Default.Send<string>(String.Format("Unable to load config file {0}, {1}", File_SystemParaCfg, ex.Message), "ShowError");
                throw new Exception(ex.Message);
            }
            #endregion

            #region >>>> UserCfg init
            try
            {
                var json_string = File.ReadAllText(File_UserCfg);
                UserCfgMgr = JsonConvert.DeserializeObject<UserCfgManager>(json_string);
            }
            catch (Exception ex)
            {
                Messenger.Default.Send<string>(String.Format("Unable to load config file {0}, {1}", File_UserCfg, ex.Message), "ShowError");
                throw new Exception(ex.Message);
            }
            #endregion
        }
        public void SaveConfig(EnumConfigType cfgType, object[] listObj)
        {
            if (listObj == null)
                throw new Exception(string.Format("保存的{0}数据为空", cfgType.ToString()));
            string fileSaved = null;
            object objSaved = null;
            switch (cfgType)
            {
                case EnumConfigType.HardwareCfg:
                    //fileSaved = File_HardwareCfg;
                    //objSaved=new HardwareCfgManager() {  }
                    break; 
                case EnumConfigType.SoftwareCfg:
                    fileSaved = File_SoftwareCfg;
                    break;
                case EnumConfigType.SystemParaCfg:
                    fileSaved = File_SystemParaCfg;
                    objSaved = new SystemParaCfgManager();
                    (objSaved as SystemParaCfgManager).SystemParaModels = listObj as SystemParaModel[];
                    SystemParaCfgMgr = objSaved as SystemParaCfgManager;
                    break;
                case EnumConfigType.UserCfg:
                    break;
                default:
                    break;
            }
            string json_str = JsonConvert.SerializeObject(objSaved);
            File.WriteAllText(fileSaved, json_str);
        }
    }
}