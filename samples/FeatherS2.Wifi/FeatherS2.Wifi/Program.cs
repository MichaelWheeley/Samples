//// https://github.com/nanoframework/Samples/blob/main/samples/Wifi/ScanWiFi/Program.cs

////#define USING_OLED
#define FEATHER_S2 // https://www.adafruit.com/product/4769
////#define HUZZAH32 // https://www.adafruit.com/product/3405

#if USING_OLED && ((FEATHER_S2 && HUZZAH32) || !(FEATHER_S2 || HUZZAH32))
#error if OLED then exactly one of FEATHER_S2 or HUZZAH32 must be selected
#endif

namespace ScanWiFi
{
    using System;

#if USING_OLED && FEATHER_S2
    using System.Device.Gpio;
#endif

    using System.Device.I2c;
    using System.Diagnostics;
    using System.Threading;

#if USING_OLED
    using Iot.Device.Ssd13xx;
    using Iot.Device.Ssd13xx.Samples;
    #endif

    using nanoFramework.Hardware.Esp32;
    using Windows.Devices.WiFi;

    public class Program
    {
        private const string MYSSID = "MYSSID";

        private const string MYPASSWORD = "MYPASSWORD";

        private const int MaxOurDevicesCount = 5;

#if USING_OLED
        private static Ssd1306 ssd1306;
#endif

        private static AutoResetEvent startWifiScanEvent;

        private static AutoResetEvent completeWifiScanEvent;

        public static void Main()
        {
#if USING_OLED
            GpioController gpioController;
            GpioPin outputPowerEnablePin;

            // setup display
            {
#if FEATHER_S2
                gpioController = new GpioController(PinNumberingScheme.Logical);
                outputPowerEnablePin = gpioController.OpenPin(Gpio.IO21, PinMode.Output);
                outputPowerEnablePin.Write(PinValue.High);

                Configuration.SetPinFunction(Gpio.IO08, DeviceFunction.I2C1_DATA);
                Configuration.SetPinFunction(Gpio.IO09, DeviceFunction.I2C1_CLOCK);

#elif HUZZAH32
                Configuration.SetPinFunction(Gpio.IO23, DeviceFunction.I2C1_DATA);
                Configuration.SetPinFunction(Gpio.IO22, DeviceFunction.I2C1_CLOCK);
#endif

                var i2c = I2cDevice.Create(new I2cConnectionSettings(1, 0x3c, I2cBusSpeed.StandardMode));
                ssd1306 = new Ssd1306(i2c, Ssd13xx.DisplayResolution.OLED128x32);
                ssd1306.Font = new BasicFont();
            }
#endif

            startWifiScanEvent = new AutoResetEvent(false);
            completeWifiScanEvent = new AutoResetEvent(false);

            // start Wifi scan controller
            {
                var threadStart = new ThreadStart(() =>
                {
                    try
                    {
                        using (var wifi = WiFiAdapter.FindAllAdapters()[0])
                        {
                            wifi.AvailableNetworksChanged += Wifi_AvailableNetworksChanged;
                            while (true)
                            {
                                startWifiScanEvent.WaitOne();
                                Debug.WriteLine("starting WiFi scan");
                                wifi.ScanAsync();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("message:" + ex.Message);
                        Debug.WriteLine("stack:" + ex.StackTrace);
                    }
                });

                var thread = new Thread(threadStart);
                thread.Start();
            }

            {
                Thread.Sleep(TimeSpan.FromMilliseconds(100));
#if USING_OLED
                ssd1306.ClearScreen();
                ssd1306.DrawHorizontalLine(0, 0, 128);
                ssd1306.DrawHorizontalLine(0, 31, 128);
#endif
                startWifiScanEvent.Set();
                completeWifiScanEvent.WaitOne(60000, false);
#if USING_OLED
                ssd1306.DrawVerticalLine(0, 0, 32);
                ssd1306.DrawVerticalLine(127, 0, 32);
                ssd1306.Display();
#endif
            }

#if USING_OLED
            if (outputPowerEnablePin != null)
            {
                outputPowerEnablePin.Dispose();
            }

            if (gpioController != null)
            {
                gpioController.Dispose();
            }
#endif

            Sleep.EnableWakeupByTimer(TimeSpan.FromMinutes(1));
            Sleep.StartDeepSleep();
        }

        /// <summary>
        /// Event handler for when WiFi scan completes
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void Wifi_AvailableNetworksChanged(WiFiAdapter sender, object e)
        {
            int ourNetCount = 0;
            var ourNet = new WiFiAvailableNetwork[MaxOurDevicesCount];
            {
                foreach (var net in sender.NetworkReport.AvailableNetworks)
                {
                    Debug.WriteLine($"SSID: {net.Ssid}, BSSID: {net.Bsid}, RSSI: {net.NetworkRssiInDecibelMilliwatts}, signalLevel: {net.SignalBars}");
                    if (net.Ssid == MYSSID && ourNetCount < MaxOurDevicesCount)
                    {
                        ourNet[ourNetCount++] = net;
                    }
                }
            }

            // find match with highest signal strength
            int ourNetMax = -1;
            for (int i = 0; i < ourNetCount; i++)
            {
                ourNetMax = (ourNetMax < 0) ? i : ((ourNet[i].NetworkRssiInDecibelMilliwatts > ourNet[ourNetMax].NetworkRssiInDecibelMilliwatts) ? i : ourNetMax);
            }

            bool connected = false;
            sender.Disconnect();
            if (ourNetMax >= 0)
            {
                connected = sender.Connect(ourNet[ourNetMax], WiFiReconnectionKind.Automatic, MYPASSWORD).ConnectionStatus == WiFiConnectionStatus.Success;
            }

            #if USING_OLED
            int line = 1;
#endif

            for (int i = 0; i < ourNetCount; i++)
            {
                string connectedStr = (connected && i == ourNetMax) ? " *" : string.Empty;
                Debug.WriteLine($"Our devices SSID: {ourNet[i].Ssid}, BSSID: {ourNet[i].Bsid}, RSSI: {ourNet[i].NetworkRssiInDecibelMilliwatts}, signalLevel: {ourNet[i].SignalBars} {connectedStr}");
#if USING_OLED
                ssd1306.Write(2, line++, $"{ourNet[i].Ssid} {ourNet[i].NetworkRssiInDecibelMilliwatts}{connectedStr}", center: false);
#endif
            }

            completeWifiScanEvent.Set();
        }
    }
}
