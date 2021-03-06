﻿using JPT_TosaTest.MotionCards.IrixiCommand;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Package;
using AxisParaLib;

namespace JPT_TosaTest.MotionCards
{
    public class IrixiEE0017
    {
        private int AXIS_NUM = 12;
        private List<AxisArgs> AxisStateList = new List<AxisArgs>();
        private static readonly object ComportLock = new object();
        SerialPort Sp = null;
        private Queue<byte> FrameRecvByteQueue = new Queue<byte>();
        private List<short> ADCRawDataList = new List<short>();
        private static Dictionary<string, IrixiEE0017> InstanceDic = new Dictionary<string, IrixiEE0017>();

        public EventHandler<UInt16?> OnOutputStateChanged;
        public EventHandler<UInt16?> OnInputStateChanged;
        public EventHandler<Tuple<byte, AxisArgs>> OnAxisStateChanged;


        private Irixi_HOST_CMD_HOME CommandHome = new Irixi_HOST_CMD_HOME();
        private Irixi_HOST_CMD_MOVE CommandMove = new Irixi_HOST_CMD_MOVE();
        private Irixi_HOST_CMD_MOVE_TRIGGER CommandMoveTrigger = new Irixi_HOST_CMD_MOVE_TRIGGER();
        private Irixi_HOST_CMD_STOP CommandStop = new Irixi_HOST_CMD_STOP();
        private Irixi_HOST_CMD_SET_ACC CommandSetMoveAcc = new Irixi_HOST_CMD_SET_ACC();
        private Irixi_HOST_CMD_GET_MCSU_STA CommandGetMcsuSta = new Irixi_HOST_CMD_GET_MCSU_STA();
        private Irixi_HOST_CMD_GET_SYS_INFO CommandGetSysInfo = new Irixi_HOST_CMD_GET_SYS_INFO();
        private Irixi_HOST_CMD_GET_MEM_LEN CommandGetMemLen = new Irixi_HOST_CMD_GET_MEM_LEN();
        private Irixi_HOST_CMD_READ_MEM CommandReadMem = new Irixi_HOST_CMD_READ_MEM();
        private Irixi_HOST_CMD_CLEAR_MEM CommandClearMem = new Irixi_HOST_CMD_CLEAR_MEM();
        private Irixi_HOST_CMD_READ_AD CommandReadAd = new Irixi_HOST_CMD_READ_AD();

        private Irixi_HOST_CMD_SET_T_ADC CommandSetTriggerADC = new Irixi_HOST_CMD_SET_T_ADC();
        private Irixi_HOST_CMD_SET_DOUT CommandSetDout = new Irixi_HOST_CMD_SET_DOUT();
        private Irixi_HOST_CMD_READ_DOUT CommandReadDout = new Irixi_HOST_CMD_READ_DOUT();
        private Irixi_HOST_CMD_READ_DIN CommandReadDin = new Irixi_HOST_CMD_READ_DIN();
        private Irixi_HOST_CMD_BLINDSEARCH CommandRunBlindSearch = new Irixi_HOST_CMD_BLINDSEARCH();
        private Irixi_HOST_CMD_GET_ERR CommandClearSysError = new Irixi_HOST_CMD_GET_ERR();
        private Irixi_HOST_CMD_SET_T_OUT CommandTriggerOut = new Irixi_HOST_CMD_SET_T_OUT();
        private Irixi_HOST_CMD_GET_ERR CommandGetErr = new Irixi_HOST_CMD_GET_ERR();
        private Irixi_HOST_CMD_SET_MODE CommandSetMode = new Irixi_HOST_CMD_SET_MODE();
        private Irixi_HOST_CMD_GET_MCSU_SETTINGS CommandGetMcsuSetting = new Irixi_HOST_CMD_GET_MCSU_SETTINGS();

        //压力传感器
        private Irixi_HOST_CMD_EN_CSS CommandEnCss = new Irixi_HOST_CMD_EN_CSS();
        private Irixi_HOST_CMD_SET_CSSTHD CommandSetCssThreshold = new Irixi_HOST_CMD_SET_CSSTHD();


        //解析包
        private AutoResetEvent ParsePackageEvent = null;
        private Task TaskParsePackage = null;
        private CancellationTokenSource ctsParsePackage = null;
        private object PackageQueueLock = new object();
        private CRC32 Crc32Instance = new CRC32();
        private UInt16 PACKAGE_HEADER = 0x7E;

        //查询轴状态线程
        private Task[] AxisStateCheckTaskList = new Task[12];
        double[] AccList = new double[12];
        byte[] ModeList = new byte[12];

        private IrixiEE0017()
        {
            for (int i = 0; i < AXIS_NUM; i++)
                AxisStateList.Add(new AxisArgs());
        }


        public static IrixiEE0017 CreateInstance(string token)
        {
            InstanceDic.TryGetValue(token, out IrixiEE0017 value);
            if (value == null)
            {
                lock (ComportLock)
                {
                    InstanceDic.TryGetValue(token, out value);
                    if (value == null)
                    {
                        InstanceDic.Add(token, new IrixiEE0017());
                    }
                }
            }
            return InstanceDic[token];
        }

        public bool Init(int ComportNo)
        {
            lock (ComportLock)
            {
                Sp = new SerialPort();
                if (Sp == null)
                    return false;
                try
                {
                    Sp.DataReceived += Comport_DataReceived1; ;
                    Sp.BaudRate = 115200;
                    Sp.PortName = $"COM{ComportNo}";
                    Sp.DataBits = 8;
                    Sp.StopBits = StopBits.One;
                    Sp.Parity = Parity.None;
                    Sp.ReadTimeout = 1000;
                    Sp.WriteTimeout = 1000;
                    //Sp.ReadTimeout = portCfg.TimeOut;
                    //Sp.WriteTimeout = portCfg.TimeOut;
                    Sp.ReceivedBytesThreshold = 1;
                    if (Sp.IsOpen)
                        Sp.Close();
                    Sp.Open();
                    if (Sp.IsOpen)
                    {
                        StartParsePackage();
                        return true;
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }
        }

        public bool Deinit()
        {
            lock (ComportLock)
            {
                try
                {
                    Sp.Close();
                    StopParsePackage();
                    return true;
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
        }

        public bool GetCurrentPos(int AxisNo, out double Pos)
        {
            lock (ComportLock)
            {
                Pos = 0;
                if (AxisNo > 12 || AxisNo < 1)
                {
                    return false;
                }
                if (GetMcsuState(AxisNo, out AxisArgs axisArgs))
                {
                    if (axisArgs != null && axisArgs.ErrorCode == 0)
                    {
                        Pos = axisArgs.CurAbsPos;
                        return true;
                    }
                    return false;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 回原点
        /// </summary>
        /// <param name="AxisNo"></param>
        /// <param name="Dir"></param>
        /// <param name="Acc"></param>
        /// <param name="Speed1"></param>
        /// <param name="Speed2"></param>
        /// <returns></returns>
        public bool Home(int AxisNo, int Dir, double Acc, double Speed1, double Speed2)    //
        {
            try
            {
                if (AxisNo > 12 || AxisNo < 1)
                {
                    return false;
                }
                SetMoveAcc(AxisNo, Acc);
                CommandHome.AxisNo = (byte)AxisNo;
                CommandHome.AccStep = (UInt16)(Acc * AxisStateList[AxisNo - 1].GainFactor);
                CommandHome.LSpeed = (byte)(Speed1);
                CommandHome.HSpeed = (byte)(Speed2);
                byte[] cmd = CommandHome.ToBytes();
                this.ExcuteCmd(cmd);
                CheckAxisState(Enumcmd.HOST_CMD_HOME, AxisNo);
                return true;
            }
            catch
            {
                return false;
            }


        }

        public bool IsHomeStop(int AxisNo)
        {

            if (AxisNo > 12 || AxisNo < 1)
            {
                return false;
            }
            if (GetMcsuState(AxisNo, out AxisArgs axisArgs))
            {
                if (axisArgs != null && axisArgs.ErrorCode == 0)
                {
                    return axisArgs.IsHomed && !axisArgs.IsBusy;
                }
            }
            return false;

        }

        public bool IsNormalStop(int AxisNo)
        {

            if (AxisNo > 12 || AxisNo < 1)
            {
                return false;
            }
            if (GetMcsuState(AxisNo, out AxisArgs axisArgs))
            {
                if (axisArgs != null)
                {
                    if (axisArgs.IsBusy)
                        return false;
                    else 
                    {
                        return true;
                    }
                }
            }
            return false;

        }

        /// <summary>
        /// 绝对运动
        /// </summary>
        /// <param name="AxisNo">映射到实际的轴号</param>
        /// <param name="Acc">绝对运动加速度</param>
        /// <param name="Speed">速度</param>
        /// <param name="Pos">绝对位置</param>
        /// <returns></returns>
        public bool MoveAbs(int AxisNo, double Acc, double Speed, double Pos)
        {
            try
            {
                if (AxisNo > 12 || AxisNo < 1)
                {
                    return false;
                }
                double RelPos = 0;
                if (GetMcsuState(AxisNo, out AxisArgs axisArgs))
                {
                    if (axisArgs != null)
                    {
                        lock (axisArgs.AxisLock)
                        {
                            if (axisArgs.ErrorCode == 0 || axisArgs.ErrorCode==0x83 || axisArgs.ErrorCode==0x84)
                            {
                                RelPos = Pos - axisArgs.CurAbsPos/axisArgs.Unit.Factor;
                            }
                        }
                    }
                }
                else
                {
                    return false;
                }

               return  MoveRel(AxisNo, Acc, Speed, RelPos);
               
            }
            catch (Exception ex)
            {
                return false;
            }

        }

        /// <summary>
        /// 绝对运动,Trigger
        /// </summary>
        /// <param name="AxisNo">映射到实际的轴号</param>
        /// <param name="Acc">绝对运动加速度</param>
        /// <param name="Speed">速度</param>
        /// <param name="Pos">绝对位置</param>
        /// <returns></returns>
        public bool MoveAbs(int AxisNo, double Acc, double Speed, double Pos, EnumTriggerType TriggerType, double Interval)
        {
            try
            {
                if (AxisNo > 12 || AxisNo < 1)
                {
                    return false;
                }
                double RelPos = 0;
                if (GetMcsuState(AxisNo, out AxisArgs axisArgs))
                {
                    if (axisArgs != null)
                    {
                        lock (axisArgs.AxisLock)
                        {
                            if (axisArgs.ErrorCode == 0 || axisArgs.ErrorCode == 0x83 || axisArgs.ErrorCode == 0x84)
                            {
                                RelPos = Pos - axisArgs.CurAbsPos / axisArgs.Unit.Factor;
                            }
                        }
                    }
                }
                else
                {
                    return false;
                }
                
                return MoveRel(AxisNo, Acc, Speed, RelPos, TriggerType, Interval);
                //return WaitLongChenck(AxisNo);
            }
            catch (Exception ex)
            {
                return false;
            }


        }

        /// <summary>
        /// 相对运动
        /// </summary>
        /// <param name="AxisNo">映射到实际的轴</param>
        /// <param name="Acc">加速度</param>
        /// <param name="Speed">速度</param>
        /// <param name="Distance">相对距离</param>
        /// <returns></returns>
        public bool MoveRel(int AxisNo, double Acc, double Speed, double Distance)
        {
            lock (ComportLock)
            {
                try
                {
                    if (AxisNo > 12 || AxisNo < 1)
                    {
                        return false;
                    }
                    SetMoveAcc(AxisNo, Acc);    //设置加速度
                    int distancePuse = Convert.ToInt32(Distance * AxisStateList[AxisNo - 1].GainFactor);
                    CommandMove.FrameLength = 0x0C;
                    CommandMove.AxisNo = (byte)AxisNo;
                    CommandMove.Distance = distancePuse;
                    CommandMove.SpeedPercent = (byte)Speed;
                    byte[] cmd = CommandMove.ToBytes();
                    this.ExcuteCmd(cmd);
                    CheckAxisState(Enumcmd.HOST_CMD_MOVE, AxisNo);
                    return true;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 相对运动 Trigger
        /// </summary>
        /// <param name="AxisNo">映射到实际的轴</param>
        /// <param name="Acc">加速度</param>
        /// <param name="Speed">速度</param>
        /// <param name="Distance">相对距离</param>
        /// <returns></returns>
        public bool MoveRel(int AxisNo, double Acc, double Speed, double Distance, EnumTriggerType TriggerType, double Interval)
        {
            lock (ComportLock)
            {
                try
                {
                    if (AxisNo > 12 || AxisNo < 1)
                    {
                        return false;
                    }
                    SetMoveAcc(AxisNo, Acc);    //设置加速度
                    int distancePuse = Convert.ToInt32(Distance * AxisStateList[AxisNo - 1].GainFactor);
                    CommandMoveTrigger.TriggerType = TriggerType == EnumTriggerType.ADC ? Enumcmd.HOST_CMD_MOVE_T_ADC : Enumcmd.HOST_CMD_MOVE_T_OUT;
                    CommandMoveTrigger.AxisNo = (byte)AxisNo;
                    CommandMoveTrigger.Distance = distancePuse;
                    CommandMoveTrigger.SpeedPercent = (byte)Speed;
                    CommandMoveTrigger.TriggerInterval = (UInt16)(Interval*AxisStateList[AxisNo - 1].GainFactor);
                    byte[] cmd = CommandMoveTrigger.ToBytes();
                    this.ExcuteCmd(cmd);
                    CheckAxisState(Enumcmd.HOST_CMD_MOVE, AxisNo);
                    return true;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }

        }

        public bool SetMoveAcc(int AxisNo, double Acc)
        {

            lock (ComportLock)
            {
                try
                {
                    if (AxisNo > 12 || AxisNo < 1)
                    {
                        return false;
                    }
                    if (AccList[AxisNo - 1] == Math.Round(Acc, 6))
                    {
                        return true;
                    }
                    CommandSetMoveAcc.AxisNo = (byte)AxisNo;
                    CommandSetMoveAcc.Acc = (UInt16)(Acc);
                    byte[] cmd = CommandSetMoveAcc.ToBytes();
                    this.ExcuteCmd(cmd);
                    AccList[AxisNo - 1] = Math.Round(Acc, 6);
                    return true;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }
        }

        public bool ReadIoInBit(int Index, out bool value)
        {
            lock (ComportLock)
            {
                value = false;
                bool bRet = ReadIoInWord(1, out int wordValue);
                value = (wordValue & (1 << (Index - 1))) == 1;
                return bRet;
            }
        }

        public bool ReadIoInWord(int StartIndex, out int value)
        {
            lock (ComportLock)
            {
                value = 0;
                try
                {
                    byte[] cmd = CommandReadDin.ToBytes();
                    this.ExcuteCmd(cmd);
                    bool bRet = CommandReadDin.WaitFinish(3000);
                    bRet &= Int32.TryParse(CommandReadDin.ReturnObject.ToString(), out value);
                    return bRet;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool ReadIoOutBit(int Index, out bool value)
        {
            lock (ComportLock)
            {
                value = false;
                try
                {
                    bool bRet = ReadIoOutWord(1, out int wordValue);
                    value = (wordValue & (1 << (Index - 1))) != 0;
                    return bRet;
                }
                catch
                {
                    return false;
                }
            }

        }

        public bool ReadIoOutWord(int StartIndex, out int value)
        {
            lock (ComportLock)
            {
                value = 0;
                try
                {
                    byte[] cmd = CommandReadDout.ToBytes();
                    lock (ComportLock)
                    {
                        this.ExcuteCmd(cmd);
                    }
                    bool bRet = CommandReadDout.WaitFinish(1000);
                    Console.WriteLine("---------ReadOutWaitOne-----------");
                    bRet &= Int32.TryParse(CommandReadDout.ReturnObject.ToString(), out value);
                    return bRet;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }
        }

        public bool WriteIoOutBit(int Index, bool value)
        {
            lock (ComportLock)
            {
                try
                {
                    if (Index < 0 || Index > 8)
                        return false;
                    CommandSetDout.GPIOChannel = (byte)Index;
                    CommandSetDout.GPIOState = value ? (byte)1 : (byte)0;
                    byte[] cmd = CommandSetDout.ToBytes();
                    this.ExcuteCmd(cmd);
                    UInt16? data = null;
                    if (ReadIoOutWord(Index, out int outputValue))
                    {
                        data = (UInt16)outputValue;
                    }
                    OnOutputStateChanged?.Invoke(this, data);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool WriteIoOutWord(int StartIndex, ushort value)
        {
            try
            {
                for (int i = StartIndex; i < StartIndex + 8; i++)
                {
                    lock (ComportLock)
                    {
                        byte GPIOChannel = (byte)StartIndex;
                        byte GPIOState = (byte)((value >> (i - StartIndex)) & 0x01);
                        WriteIoOutBit(GPIOChannel, GPIOState != 0);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }

        }

        /// <summary>
        /// 配置Trigger
        /// </summary>
        /// <param name="ChannelFlags"></param>
        /// <returns></returns>
        public bool SetTrigConfig(byte ChannelFlags)
        {
            lock (ComportLock)
            {
                try
                {
                    CommandSetTriggerADC.ADCChannelFlags = ChannelFlags;
                    byte[] cmd = CommandSetTriggerADC.ToBytes();
                    this.ExcuteCmd(cmd);
                    return true;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }
        }

        public bool GetMemLength(out UInt32 Length)
        {
            lock (ComportLock)
            {
                Length = 0;
                try
                {
                   
                    byte[] cmd = CommandGetMemLen.ToBytes();
                    this.ExcuteCmd(cmd);
                    CommandGetMemLen.WaitFinish(1000);
                    Length = (UInt32)CommandGetMemLen.ReturnObject;
                    return true;

                }
                catch (Exception ex)
                {
                    return false;
                }
            }
        }

        public bool ReadMem(UInt32 Offset, UInt32 Length, out List<Int16> RawDataList)
        {
            lock (ComportLock)
            {
                RawDataList = null;
                try
                {
                    ADCRawDataList.Clear();
                    CommandReadMem.MemOffset = Offset;
                    CommandReadMem.MemLength = Length;
                    byte[] cmd = CommandReadMem.ToBytes();
                    Sp.Write(cmd, 0, cmd.Length);
                    CommandReadMem.WaitFinish(10000);
                    RawDataList = CommandReadMem.ReturnObject as List<Int16>;
                    return true;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }

        }

        public bool ClearMem()
        {
            lock (ComportLock)
            {
                try
                {
                    byte[] cmd = CommandClearMem.ToBytes();
                    Sp.Write(cmd, 0, cmd.Length);
                    return true;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }
        }

        public bool ReadAD(EnumADCChannelFlags ChannelFlags,out List<UInt16> values)
        {
            values = new List<ushort>();
            lock (ComportLock)
            {
                try
                {
                    CommandReadAd.ADChannelFlags = ChannelFlags;
                    byte[] cmd = CommandReadAd.ToBytes();
                    this.ExcuteCmd(cmd);
                    CommandReadAd.WaitFinish(1000);
                    values = CommandReadAd.ReturnObject as List<UInt16>;
                    return true;

                }
                catch (Exception ex)
                {
                    return false;
                }
            }
        }

        public bool EnableCss(EnumCssChannel Channel, bool IsEnable)
        {
            lock (ComportLock)
            {
                try
                {
                    CommandEnCss.CssChannel = Channel;
                    CommandEnCss.IsEnable = IsEnable;
                    byte[] cmd = CommandEnCss.ToBytes();
                    this.ExcuteCmd(cmd);
                    return true;

                }
                catch (Exception ex)
                {
                    return false;
                }
            }
        }

        public bool SetCssThreshold(EnumCssChannel Channel, UInt16 LowThreshold, UInt16 HightThreshold)
        {
            lock (ComportLock)
            {
                try
                {
                    CommandSetCssThreshold.CssChannel = Channel;
                    CommandSetCssThreshold.LowThreshold = LowThreshold;
                    CommandSetCssThreshold.HightThreshold = HightThreshold;
                    byte[] cmd = CommandSetCssThreshold.ToBytes();
                    this.ExcuteCmd(cmd);
                    return true;

                }
                catch (Exception ex)
                {
                    return false;
                }
            }
        }
        /// <summary>
        /// 没有实现
        /// </summary>
        /// <param name="AxisNo"></param>
        /// <param name="Pos"></param>
        /// <returns></returns>
        public bool SetCurrentPos(int AxisNo, double Pos)
        {
            if (AxisNo > 12 || AxisNo < 1)
            {
                return false;
            }
            return false;
        }

        public bool Stop(int AxisNo)
        {
            lock (ComportLock)
            {
                try
                {
                    CommandStop.AxisNo = (byte)AxisNo;
                    byte[] cmd = CommandStop.ToBytes();
                    this.ExcuteCmd(cmd);
                    return true;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }
        }

        public bool DoBindSearch(int HAxis, int VAxis, double Range, double Gap, double Speed, double Interval)
        {
            lock (ComportLock)
            {
                try
                {
                    CommandRunBlindSearch.HAxisNo = (byte)HAxis;
                    CommandRunBlindSearch.VAxisNo = (byte)VAxis;
                    int AxisIndex = (int)HAxis;
                    CommandRunBlindSearch.Range = (uint)(Range * AxisStateList[AxisIndex].GainFactor);
                    CommandRunBlindSearch.Gap = (uint)(Gap * AxisStateList[AxisIndex].GainFactor);
                    CommandRunBlindSearch.SpeedPercent = (byte)((Speed * AxisStateList[AxisIndex].GainFactor) / 100);
                    CommandRunBlindSearch.Interval = (UInt16)(Interval * AxisStateList[AxisIndex].GainFactor);
                    byte[] cmd = CommandRunBlindSearch.ToBytes();
                    CheckAxisState(Enumcmd.HOST_CMD_BLINDSEARCH, HAxis);
                    CheckAxisState(Enumcmd.HOST_CMD_BLINDSEARCH, VAxis);
                    this.ExcuteCmd(cmd);
                    return true;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }
        }

        public bool SetAxisPara(int AxisNo, UInt32 GainFactor, double LimitP, double LimitN, double HomeOffset, int HomeMode, string AxisName = "")
        {
            if (AxisNo > 12 || AxisNo < 1)
            {
                return false;
            }
            AxisStateList[AxisNo - 1].GainFactor = (int)GainFactor;
            AxisStateList[AxisNo - 1].LimitP = LimitP;
            AxisStateList[AxisNo - 1].LimitN = LimitN;
            AxisStateList[AxisNo - 1].HomeMode = HomeMode;
            AxisStateList[AxisNo - 1].HomeOffset = HomeOffset;
            return true;
        }

        #region Private
        private bool GetMcsuState(int AxisNo, out AxisArgs axisargs)
        {
            lock (ComportLock)
            {
                axisargs = null;
                try
                {
                    if (AxisNo > 12 || AxisNo < 1)
                    {
                        return false;
                    }
                    //Command
                    CommandGetMcsuSta.AxisNo = (byte)AxisNo;
                    byte[] cmd = CommandGetMcsuSta.ToBytes();
                    this.ExcuteCmd(cmd);
                    if (CommandGetMcsuSta.WaitFinish(1000))
                    {
                        var state = CommandGetMcsuSta.ReturnObject as MCSUS_STATE;
                        int axisIndex = state.AxisIndex;
                        lock (AxisStateList[axisIndex - 1].AxisLock)
                        {
                            AxisStateList[axisIndex - 1].IsHomed = state.IsHomed;
                            AxisStateList[axisIndex - 1].IsBusy = state.IsBusy;
                            AxisStateList[axisIndex - 1].ErrorCode = state.Error;
                            AxisStateList[axisIndex - 1].CurAbsPos = (double)state.AbsPosition / (double)AxisStateList[axisIndex - 1].GainFactor;
                        }
                        axisargs = AxisStateList[axisIndex - 1];
                    }

                    else
                        return false;
                    return axisargs != null;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }
        }


        private List<byte> TempList = new List<byte>();
        private int ExpectLength = 0;
        private void Comport_DataReceived1(object sender, SerialDataReceivedEventArgs e)
        {
            int Len = Sp.BytesToRead;
            for (int i = 0; i < Len; i++)
            {
                byte bt = (byte)Sp.ReadByte();
                FrameRecvByteQueue.Enqueue(bt);
            }

            while (FrameRecvByteQueue.Count > 0)
            {
                byte bt = FrameRecvByteQueue.Dequeue();
                if (bt == PACKAGE_HEADER && TempList.Count == 0)
                {
                    TempList.Add(bt);
                    continue;
                }
                if (TempList.Count > 0) //说明发现了包
                {
                    TempList.Add(bt);
                    if (TempList.Count == 3)
                    {
                        ExpectLength = TempList[1] + TempList[2] * 256;
                    }
                    else if (TempList.Count >= (7 + ExpectLength))
                    {
                        byte[] dataList = TempList.ToArray();
                        UInt32 Crc32 = (UInt32)(dataList[dataList.Length - 4] + (dataList[dataList.Length - 3] << 8) + (dataList[dataList.Length - 2] << 16) + (dataList[dataList.Length - 1] << 24));
                        UInt32 CalcCrc32 = Crc32Instance.Calculate(dataList, 0, dataList.Length - 4);
                       
                        if (Crc32 == CalcCrc32) //校验成功
                        {
                            ProcessPackage(dataList);
                        }
                        ExpectLength = 0;
                        TempList = new List<byte>();
                    }
                }
            }
        }
        private void ExcuteCmd(byte[] Cmd)
        {

            Sp.Write(Cmd, 0, Cmd.Length);

        }

        private void StartParsePackage()
        {
            #region 解析包放在中断中进行
            //long TickStart = 0;
            //if (TaskParsePackage == null || TaskParsePackage.IsCanceled || TaskParsePackage.IsCompleted)
            //{
            //ctsParsePackage = new CancellationTokenSource();

            //TaskParsePackage = new Task(() =>
            //{
            //    TickStart = DateTime.Now.Ticks;
            //    List<byte> TempList = new List<byte>();
            //    int ExpectLength = 0;
            //    while (!ctsParsePackage.IsCancellationRequested)
            //    {
            //        Thread.Sleep(1);
            //        if (FrameRecvByteQueue.Count > 0)
            //        {
            //            byte data = 0x00;
            //            lock (PackageQueueLock)
            //            {
            //                try
            //                {
            //                    data = FrameRecvByteQueue.Dequeue();
            //                }
            //                catch (InvalidOperationException ex)
            //                {
            //                    continue;
            //                }
            //            }
            //            if (data == PACKAGE_HEADER && TempList.Count == 0)
            //            {
            //                TempList.Add(data);
            //            }
            //            else if (TempList.Count > 0)
            //            {
            //                TempList.Add(data);
            //                if (TempList.Count == 3)
            //                {
            //                    ExpectLength = TempList[1] + (TempList[2] << 8);
            //                }
            //                else if (ExpectLength > 0)
            //                {
            //                    if (TempList.Count == ExpectLength + 7)
            //                    {
            //                        byte[] dataList = TempList.ToArray();
            //                        UInt32 Crc32 = (UInt32)(dataList[dataList.Length - 4] + (dataList[dataList.Length - 3] << 8) + (dataList[dataList.Length - 2] << 16) + (dataList[dataList.Length - 1] << 24));
            //                        UInt32 CalcCrc32 = Crc32Instance.Calculate(dataList, 0, dataList.Length - 4);
            //                        if (Crc32 == CalcCrc32) //校验成功
            //                        {
            //                            ProcessPackage(dataList);
            //                        }
            //                        ExpectLength = 0;
            //                        TempList = new List<byte>();
            //                    }
            //                }
            //            }
            //        }
            //        else
            //        {

            //        }
            //    }

            //}, ctsParsePackage.Token);
            //TaskParsePackage.Start();
            //}
            #endregion
            GetStateWhenFirstLoad();
        }
        //处理收到的包
        private void ProcessPackage(byte[] data)
        {
            byte Cmd = data[6];
            int RealLen = data.Length;
            switch (Cmd)
            {
                case (byte)Enumcmd.HOST_CMD_GET_MCSU_STA:      //读取状态值
                    CommandGetMcsuSta.GetDataFromRowByteArr(data);
                    MCSUS_STATE state = CommandGetMcsuSta.ReturnObject as MCSUS_STATE;
                    //int axisIndex = state.AxisIndex;
                    //lock (AxisStateList[axisIndex - 1].AxisLock)
                    //{
                    //    AxisStateList[axisIndex - 1].IsHomed = state.IsHomed;
                    //    AxisStateList[axisIndex - 1].IsBusy = state.IsBusy;
                    //    AxisStateList[axisIndex - 1].ErrorCode = state.Error;
                    //    AxisStateList[axisIndex - 1].CurAbsPos = (double)state.AbsPosition / (double)AxisStateList[axisIndex - 1].GainFactor;
                    //}
                    CommandGetMcsuSta.SetSyncFlag();
                    break;
                case (byte)Enumcmd.HOST_CMD_GET_MEM_LEN:
                    CommandGetMemLen.GetDataFromRowByteArr(data);
                    CommandGetMemLen.SetSyncFlag();
                    break;
                case (byte)Enumcmd.HOST_CMD_READ_MEM:
                    CommandReadMem.GetDataFromRowByteArr(data);
                    CommandReadMem.SetSyncFlag();

                    break;
                case (byte)Enumcmd.HOST_CMD_READ_DIN:
                    CommandReadDin.GetDataFromRowByteArr(data);  //解析包
                    CommandReadDin.SetSyncFlag();   //通知读取完毕
                    Console.WriteLine("---------ReadInOver-----------");
                    break;
                case (byte)Enumcmd.HOST_CMD_READ_DOUT:
                    CommandReadDout.GetDataFromRowByteArr(data);
                    CommandReadDout.SetSyncFlag();

                    Console.WriteLine("---------ReadOutSetOver-----------");
                    break;
                case (byte)Enumcmd.HOST_CMD_GET_ERR:
                    CommandGetErr.GetDataFromRowByteArr(data);
                    CommandGetErr.SetSyncFlag();
                    break;
                case (byte)Enumcmd.HOST_CMD_GET_MCSU_SETTINGS:
                    CommandGetMcsuSetting.GetDataFromRowByteArr(data);
                    CommandGetErr.SetSyncFlag();
                    break;
                case (byte)Enumcmd.HOST_CMD_READ_AD:
                    CommandReadAd.GetDataFromRowByteArr(data);
                    CommandReadAd.SetSyncFlag();
                    break;
            }

        }

        private void StopParsePackage()
        {
            ParsePackageEvent.Set();
            if (ctsParsePackage != null)
                ctsParsePackage.Cancel();
        }

        private void CheckAxisState(Enumcmd Command, int Index) //触发一次查询1秒 
        {
            int IndexBase0 = Index - 1;
            AxisStateList[IndexBase0].ReqStartTime = DateTime.Now.Ticks;
            if (AxisStateCheckTaskList[IndexBase0] == null || AxisStateCheckTaskList[IndexBase0].IsCanceled || AxisStateCheckTaskList[IndexBase0].IsCompleted)
            {
                AxisStateCheckTaskList[IndexBase0] = new Task(() =>
                {
                    while (true)
                    {
                        if (this.GetMcsuState(Index, out AxisArgs state))   //更新状态
                        {
                            OnAxisStateChanged?.Invoke(this, new Tuple<byte, AxisArgs>((byte)(Index), state));
                            if (AxisStateList[IndexBase0].IsBusy)   //如果电机在忙就一直查询
                                AxisStateList[IndexBase0].ReqStartTime = DateTime.Now.Ticks;
                        }
                        Thread.Sleep(10);
                        if (TimeSpan.FromTicks(DateTime.Now.Ticks - AxisStateList[IndexBase0].ReqStartTime).TotalSeconds > 1)
                        {
                            break;
                        }
                    }
                    AxisStateList[IndexBase0].IsInRequest = false;
                });
                AxisStateCheckTaskList[IndexBase0].Start();
            }
        }

        //private bool WaitLongChenck(int Index)
        //{
        //    int IndexBase0 = Index - 1;
        //    while (true)
        //    {
        //        Thread.Sleep(100);
        //        if (this.GetMcsuState(Index, out AxisArgs state))   //更新状态
        //        {
        //            OnAxisStateChanged?.Invoke(this, new Tuple<byte, AxisArgs>((byte)(Index), state));
        //            if (state.IsBusy)   //如果电机在忙就一直查询
        //                AxisStateList[IndexBase0].ReqStartTime = DateTime.Now.Ticks;
        //            else
        //                break;
                   
        //            if (TimeSpan.FromTicks(DateTime.Now.Ticks - AxisStateList[IndexBase0].ReqStartTime).TotalSeconds > 1)
        //            {
        //               throw new Exception($"Timeout when check axis {IndexBase0}") ;
        //            }
        //        }
        //    }
        //    return true;
        //}

        /// <summary>
        /// 设置电机的控制方式，D+P， CW-CCW，回原点方向等
        ///uint8   0: 1-Pulse Mode, 1: 2-Pulse Mode
        ///uint8:1  0: CW=Pulse, CCW=DIR, 1: CW=DIR, CCW=PULSE
        ///uint8:2	0: Default, 1: Reversed
        ///uint8:3	0: Default, 1: Reversed
        ///uint8:4	0: Ignore, 1: Detect
        ///uint8:5	0: active high, 1: active low
        /// </summary>
        /// <returns></returns>
        public bool SetMode(int AxisNo,byte mode)
        {
            lock (ComportLock)
            {
                try
                {
                    if (AxisNo > 12 || AxisNo < 1)
                    {
                        return false;
                    }
                    if (mode == ModeList[AxisNo - 1]) //如果模式没有改变就无需设置
                        return true;
                    CommandSetMode.AxisNo = (byte)AxisNo;
                    CommandSetMode.Mode = mode;
                    byte[] cmd = CommandSetMode.ToBytes();
                    this.ExcuteCmd(cmd);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool GetMcsuSetting(int AxisNo,out byte mode,out double Acc)
        {
            mode = 0;
            Acc = 0;

            return false;
        }

        public bool GetLastError(out byte McsuID, out byte Error)
        {
            McsuID = 0xFF;
            Error = 0;
            lock (ComportLock)
            {
                try
                {
                    byte[] cmd = CommandGetErr.ToBytes();
                    this.ExcuteCmd(cmd);
                    bool bRet = CommandGetErr.WaitFinish(1000);
                    Console.WriteLine("---------ReadOutWaitOne-----------");
                    var tuple = CommandGetErr.ReturnObject as Tuple<byte, byte>;
                    if (tuple != null)
                    {
                        McsuID = tuple.Item1;
                        Error = tuple.Item2;
                        return true;
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
            }

        }



        private async void GetStateWhenFirstLoad()
        {
            //第一次需要先查询一下位置
            await Task.Run(() =>
            {
                Thread.Sleep(5000);
                for (int i = 0; i < 10; i++)
                {
                    Thread.Sleep(500);
                    UInt16? realValue = null;
                    if (ReadIoOutWord(0, out int value))
                    {
                        realValue = (UInt16)value;
                    }
                    OnOutputStateChanged?.Invoke(this, realValue);
                    if (i == 9 || realValue.HasValue)
                        break;
                }

                for (int i = 1; i <= 12; i++)
                {
                    CheckAxisState(Enumcmd.HOST_CMD_MOVE, i);
                }
            });
           
        }
        #endregion
    }
}
