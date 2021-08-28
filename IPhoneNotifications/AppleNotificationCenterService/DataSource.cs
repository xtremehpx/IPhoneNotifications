﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using System.Runtime.InteropServices.WindowsRuntime;

namespace IPhoneNotifications.AppleNotificationCenterService
{
    public class DataSource
    {
        public readonly GattCharacteristic GattCharacteristic;
        private bool isValueChangedHandlerRegistered = false;

        public event Action<NotificationAttributeCollection> NotificationAttributesReceived;
        public event Action<ApplicationAttributeCollection> ApplicationAttributesReceived;

        public DataSource(GattCharacteristic characteristic)
        {
            GattCharacteristic = characteristic;
        }

        public GattCharacteristicProperties CharacteristicProperties
        {
            get
            {
                return GattCharacteristic.CharacteristicProperties;
            }
        }

        private void AddValueChangedHandler()
        {
            if (!isValueChangedHandlerRegistered)
            {
                GattCharacteristic.ValueChanged += GattCharacteristic_ValueChanged;
                isValueChangedHandlerRegistered = true;
            }
        }

        private void RemoveValueChangedHandler()
        {
            if (isValueChangedHandlerRegistered)
            {
                GattCharacteristic.ValueChanged -= GattCharacteristic_ValueChanged;
                isValueChangedHandlerRegistered = false;
            }
        }

        public async Task<bool> UnsubscribeAsync()
        {

            try
            {
                RemoveValueChangedHandler();

                var result = await
                        GattCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.None);
                if (result != GattCommunicationStatus.Success)
                {
                    System.Diagnostics.Debug.WriteLine("Error deregistering for notifications: {result}");
                    return false;
                }
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine(e.Message);
            }
            return true;
        }

        public async Task<bool> SubscribeAsync()
        {
            if (GattCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
            {
                try
                {
                    // BT_Code: Must write the CCCD in order for server to send notifications.
                    // We receive them in the ValueChanged event handler.
                    // Note that this sample configures either Indicate or Notify, but not both.
                    var result = await
                            GattCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                                GattClientCharacteristicConfigurationDescriptorValue.Notify);
                    if (result == GattCommunicationStatus.Success)
                    {
                        AddValueChangedHandler();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Error registering for notifications: {result}");
                        return false;
                    }
                }
                catch (Exception e)
                {
                    RemoveValueChangedHandler();
                    //throw e;
                    System.Diagnostics.Debug.WriteLine("Data source subscribe failed. " + e.Message);
                    return false;
                }
            }
            else
            {
                // throw new Exception("Data source characteristic does not have a notify property.");
                System.Diagnostics.Debug.WriteLine("Data source characteristic does not have a notify property.");
                return false;
            }
            return true;
        }
        
        private void GattCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            CommandID commandID = (CommandID)args.CharacteristicValue.GetByte(0);

            switch (commandID)
            {
                case CommandID.GetAppAttributes:
                    ApplicationAttributesReceived?.Invoke(new ApplicationAttributeCollection(args.CharacteristicValue));
                    break;
                case CommandID.GetNotificationAttributes:
                    NotificationAttributesReceived?.Invoke(new NotificationAttributeCollection(args.CharacteristicValue));
                    break;
                case CommandID.PerformNotificationAction:
                    break;
            }
        }
    }
}
