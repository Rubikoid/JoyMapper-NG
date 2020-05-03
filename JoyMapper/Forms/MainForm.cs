﻿using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Threading;
using System.Linq;
using SharpDX.DirectInput;
using JoyMapper.Controller;
using JoyMapper.Controller.Internal;
using JoyMapper.FFB;
using System.ComponentModel;
using System.Reflection;

namespace JoyMapper {
    public partial class MainForm : Form {
        private static NLog.Logger logger = NLog.LogManager.GetLogger("MainForm");
        public static IntPtr PublicHandle { get; private set; }
        private Thread bg_thread = null;

        class FFBEvent {
            public string Name { get; set; }
            public int Count { get; set; }
        }
        private BindingList<FFBEvent> data = new BindingList<FFBEvent>() { };
        private BindingList<FFBEvent> data2 = new BindingList<FFBEvent>() { };
        private IEnumerable<IController> conrts;

        public MainForm() {
            InitializeComponent();
            PublicHandle = Handle;

            logger.Info($"Loading joymapper version: {Assembly.GetExecutingAssembly().GetName().Version.ToString()}");

            VirtualFFBPacketHandler.Init();
            this.loadControllers();

            this.dataGridView1.DataSource = data;
            this.dataGridView2.DataSource = data2;

            ControllerCache.vc.FFBDataReceived += this.callb_kek;

            ControllerCache.vc.FFBDataReceived += this.callb1;
            ControllerCache.vc2.FFBDataReceived += this.callb2;
        }

        private void DoWork(object data) {
            conrts = data as IEnumerable<IController>;
            try {
                logger.Info("Connecting to controllers");
                foreach (IController gc in conrts) gc.Connect();
                ControllerCache.vc.Connect();
                ControllerCache.vc2.Connect();
                logger.Info("Connecting to vjoy");
                while (true) {
                    State ins = new State(ControllerCache.vc, ControllerCache.vc.ButtonCount);
                    foreach (IController gc in conrts) gc.FillExternalInfo(ref ins);
                    ControllerCache.vc.UpdateInfo(ins);
                    ControllerCache.vc2.UpdateInfo(ins);
                    Thread.Sleep(20);
                }
            } catch (ThreadAbortException) {
                logger.Info("Stopping thread on threadabord");
            } catch (Exception ex) {
                logger.Error(ex, "SHIT SHIT");
            } finally {
                foreach (IController gc in conrts) gc.Disconnect();
                ControllerCache.vc.Disconnect();
                ControllerCache.vc2.Disconnect();
                logger.Info("Disconnected from controllers");
            }
        }

        private void loadControllers() {
            ControllerCache.Update((name, cont) => { this.GameControllers.Items.Add(cont.ToString()); });
            logger.Info("Controllers updated");
        }

        private void button1_Click(object sender, EventArgs e) {
            IEnumerable<GameController> controllers = ControllerCache.controllerDictionary
                .Where(x => this.GameControllers.CheckedItems.Contains(x.Key))
                .Select(x => x.Value);
            //this.GameControllers.Enabled = false;
            this.StartBtn.Enabled = false;
            this.StopBtn.Enabled = true;
            this.bg_thread = new Thread(this.DoWork);
            this.bg_thread.Start(controllers);
        }

        private void button2_Click(object sender, EventArgs e) {
            this.bg_thread?.Abort();
            //this.GameControllers.Enabled = true;
            this.StartBtn.Enabled = true;
            this.StopBtn.Enabled = false;
            // GameController c = this.cboGameController.SelectedValue as GameController;
        }

        private void UpdateBtn_Click(object sender, EventArgs e) {
            this.loadControllers();
        }

        private void CreateMapBtn_Click(object sender, EventArgs e) {
            string contName = this.GameControllers.SelectedItem as string;
            if (contName != null && ControllerCache.controllerDictionary.ContainsKey(contName)) {
                ControllerMap mapForm = new ControllerMap(ControllerCache.controllerDictionary[contName]);
                mapForm.Show();
            }
        }

        public void callb_kek(VirtualFFBPacket e) {
            if (this.bg_thread.IsAlive) {
                foreach (IController gc in conrts) {
                    foreach (FFBMap map in gc.Mappings.OfType<FFBMap>()) {
                        // logger.Debug($"Running mapping on {gc.ToString()}");
                        map.Map(e);
                    }
                }
            }
        }


        public void callb1(VirtualFFBPacket e) {
            string key = e._FFBPType.ToString();
            this.dataGridView1.Invoke((MethodInvoker)(() => {
                FFBEvent ev = this.data.FirstOrDefault(x=>x.Name == key);
                if (ev != null) {
                    ev.Count++;
                    this.data.ResetItem(this.data.IndexOf(ev));
                } else
                    this.data.Add(new FFBEvent() { Name = e._FFBPType.ToString(), Count = 1 });
            }));
        }

        public void callb2(VirtualFFBPacket e) {
            string key = e._FFBPType.ToString();
            this.dataGridView2.Invoke((MethodInvoker)(() => {
                FFBEvent ev = this.data2.FirstOrDefault(x=>x.Name == key);
                if (ev != null) {
                    ev.Count++;
                    this.data2.ResetItem(this.data2.IndexOf(ev));
                } else
                    this.data2.Add(new FFBEvent() { Name = e._FFBPType.ToString(), Count = 1 });
            }));
        }
        private void DoOnSelected(object sender, Action<GameController> action) {
            var _cont = ControllerCache.controllerDictionary
                .Where(x => x.Value.ToString() == (string)this.GameControllers.SelectedItem)
                .DefaultIfEmpty(new KeyValuePair<string, GameController>("", null))
                .FirstOrDefault();
            if (_cont.Value != null) {
                var cont = _cont.Value;
                if (cont.Connected) {
                    try {
                        action(cont);
                    } catch (Exception ex) {
                        if (sender != null) {
                            logger.Warn($"WTF CONTROLLER {cont} {ex}");
                            Thread.Sleep(200);
                            DoOnSelected(null, action);
                        } else {
                            logger.Error($"WTF CONTROLLER {cont} DIEEE {ex}");
                        }
                    }
                } else {
                    if (sender != null) {
                        logger.Warn($"WTF CONTROLLER {cont} DISCONNECTD -> RECONNECT??");
                        cont.Connect();
                        Thread.Sleep(10);
                        DoOnSelected(null, action);
                    } else
                        logger.Error($"WTF CONTROLLER {cont} DIE??");
                }
            }
        }
        private void button1_Click_1(object sender, EventArgs e) {
            this.DoOnSelected(sender, (cont) => {
                var effpar = new EffectParameters() {
                    Duration = -1,
                    Flags = EffectFlags.Cartesian | EffectFlags.ObjectIds,
                    Gain = 100,
                    SamplePeriod = 0,
                    StartDelay = 0,
                    TriggerButton = -1,
                    TriggerRepeatInterval = 0,
                    Envelope = null,

                    Parameters = new ConstantForce() {
                        Magnitude = 100,
                    }
                };
                effpar.SetAxes(new int[1] { cont.FFBAxes[0] }, new int[1] { 1 });

                Effect newE = new Effect(cont.joystick, EffectGuid.ConstantForce, effpar);

                cont.RunExclusive(() => newE.Start());
                logger.Info($"{newE.Status}");
            });
        }

        private void button2_Click_1(object sender, EventArgs e) {
            this.DoOnSelected(sender, (cont) => {
                cont.SendFFBCommand(ForceFeedbackCommand.StopAll);
                cont.SendFFBCommand(ForceFeedbackCommand.Reset);
                cont.SendFFBCommand(ForceFeedbackCommand.SetActuatorsOn);
                cont.SendFFBCommand(ForceFeedbackCommand.Continue);
            });
        }

        private void button3_Click(object sender, EventArgs e) {
            this.DoOnSelected(sender, (cont) => {
                cont.SendFFBCommand(ForceFeedbackCommand.Pause);
            });
        }

        private void button4_Click(object sender, EventArgs e) {
            this.DoOnSelected(sender, (cont) => {
                cont.SendFFBCommand(ForceFeedbackCommand.Continue);
            });
        }

        private void button5_Click(object sender, EventArgs e) {
            this.DoOnSelected(sender, (cont) => {
                var x = cont.joystick.GetEffects();
                foreach (var y in x) {
                    logger.Info($"{y.Guid}, {y.Name}, {y.Type}");
                }
            });
        }
    }
}
