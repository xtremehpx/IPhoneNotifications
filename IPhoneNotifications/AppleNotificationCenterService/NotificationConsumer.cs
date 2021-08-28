using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.ApplicationModel.Activation;
using Microsoft.QueryStringDotNET;
using Windows.Devices.Enumeration;

namespace IPhoneNotifications.AppleNotificationCenterService
{
    public class NotificationConsumer
    {
        public string BluetoothLEDeviceId;
        public string BluetoothLEDeviceName = "No device selected";

        private BluetoothLEDevice bluetoothLeDevice = null;

        private GattDeviceService GattService = null;

        public NotificationSource NotificationSource;
        public ControlPoint ControlPoint;
        public DataSource DataSource;

        private Dictionary<UInt32, NotificationSourceData> Notifications;
        private Dictionary<string, ApplicationAttributeCollection> Applications;
        private Dictionary<string, Queue<NotificationAttributeCollection>> ApplicationNotificationQueue;


        public event TypedEventHandler<NotificationConsumer, AppleNotificationEventArgs> NotificationAdded;
        public event TypedEventHandler<NotificationConsumer, AppleNotificationEventArgs> NotificationModified;
        public event TypedEventHandler<NotificationConsumer, NotificationSourceData> NotificationRemoved;

        public static Action<IActivatedEventArgs> OnToastNotification = args => { };

        private bool subscribedForNotifications = false;

        public NotificationConsumer()
        {
            Applications = new Dictionary<string, ApplicationAttributeCollection>();
            Notifications = new Dictionary<UInt32, NotificationSourceData>();
            ApplicationNotificationQueue = new Dictionary<string, Queue<NotificationAttributeCollection>>();

            OnToastNotification = OnToastNotificationReceived;
        }


        private static Guid ancsUuid = new Guid("7905F431-B5CE-4E99-A40F-4B1E122D00D0");
        private static Guid notificationSourceUuid = new Guid("9FBF120D-6301-42D9-8C58-25E699A21DBD");
        private static Guid controlPointUuid = new Guid("69D1D8F3-45E1-49A8-9821-9BBDFDAAD9D9");
        private static Guid dataSourceUuid = new Guid("22EAC6E9-24D6-4BB5-BE44-B36ACE7C7BFB");

        public async void Connect()
        {
            if (!await ClearBluetoothLEDevice())
            {
                System.Diagnostics.Debug.WriteLine("Error: Unable to reset state, try again.");
                //throw new Exception("Error: Unable to reset state, try again.");
                return;
            }

            try
            {
                // BT_Code: BluetoothLEDevice.FromIdAsync must be called from a UI thread because it may prompt for consent.
                bluetoothLeDevice = await BluetoothLEDevice.FromIdAsync(BluetoothLEDeviceId);

                if (bluetoothLeDevice == null)
                {
                    throw new Exception("Failed to connect to device.");
                }
                System.Diagnostics.Debug.WriteLine("Device connected is: " + bluetoothLeDevice.ConnectionStatus);

                //bluetoothLeDevice.ConnectionStatusChanged += BluetoothLeDevice_ConnectionStatusChanged;

            }
            catch (Exception ex) when ((uint)ex.HResult == 0x800710df)
            {
                // ERROR_DEVICE_NOT_AVAILABLE because the Bluetooth radio is not on.
                throw new Exception("ERROR_DEVICE_NOT_AVAILABLE because the Bluetooth radio is not on.");
            }

            if (bluetoothLeDevice != null)
            {
                try
                {
                    var ret = await bluetoothLeDevice.GetGattServicesForUuidAsync(ancsUuid);
                    GattService = ret.Services[0];

                    if (GattService == null)
                    {
                        throw new Exception("Apple Notification Center Service not found.");
                    }

                    var accessStatus = await GattService.RequestAccessAsync();
                    if (accessStatus == DeviceAccessStatus.Allowed)
                    {
                        var rett = await GattService.GetCharacteristicsForUuidAsync(controlPointUuid);
                        ControlPoint = new ControlPoint(rett.Characteristics[0]);

                        rett = await GattService.GetCharacteristicsForUuidAsync(dataSourceUuid);
                        DataSource = new DataSource(rett.Characteristics[0]);

                        rett = await GattService.GetCharacteristicsForUuidAsync(notificationSourceUuid);
                        NotificationSource = new NotificationSource(rett.Characteristics[0]);
                    }
                    else
                    {
                        throw new Exception("Access to ANCS is not granted: " + accessStatus.ToString());
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception(ex.Message);
                }

                if (bluetoothLeDevice.ConnectionStatus == Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected)
                {
                    if (DataSource != null && DataSource.GattCharacteristic.Service != null)
                    {
                        bool ret = await DataSource.SubscribeAsync();
                        if (ret)
                        {
                            DataSource.ApplicationAttributesReceived += DataSource_ApplicationAttributesReceived;
                            DataSource.NotificationAttributesReceived += DataSource_NotificationAttributesReceived;
                        }
                    }

                    if (NotificationSource != null && NotificationSource.GattCharacteristic.Service != null)
                    {
                        bool ret = await NotificationSource.SubscribeAsync();
                        if (ret)
                        {

                            NotificationSource.ValueChanged += NotificationSource_ValueChanged;
                        }
                    }

                }
                else
                {
                    DataSource.ApplicationAttributesReceived -= DataSource_ApplicationAttributesReceived;
                    DataSource.NotificationAttributesReceived -= DataSource_NotificationAttributesReceived;
                    NotificationSource.ValueChanged -= NotificationSource_ValueChanged;
                    subscribedForNotifications = false;
                }
            }
            else
            {
                throw new Exception("Failed to connect to device.");
            }

            System.Diagnostics.Debug.WriteLine("Device connected is: " + bluetoothLeDevice.ConnectionStatus);
        }

        private async Task<bool> ClearBluetoothLEDevice()
        {
            bool status = true;

            GattService?.Dispose();
            GattService = null;


            if (ControlPoint != null)
            {
                ControlPoint = null;
            }

            if (NotificationSource != null)
            {
                status = await NotificationSource?.UnsubscribeAsync();

                NotificationSource.ValueChanged -= NotificationSource_ValueChanged;
                NotificationSource = null;
            }

            if (DataSource != null)
            {
                status &= await DataSource?.UnsubscribeAsync();

                DataSource.NotificationAttributesReceived -= DataSource_NotificationAttributesReceived;
                DataSource = null;
            }

            //if (bluetoothLeDevice != null)
            //{
            //    bluetoothLeDevice.ConnectionStatusChanged -= BluetoothLeDevice_ConnectionStatusChanged;
            //}

            bluetoothLeDevice?.Dispose();
            bluetoothLeDevice = null;
            subscribedForNotifications = false;

            return status;
        }


        private async void BluetoothLeDevice_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            if (sender.ConnectionStatus == Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected)
            {

                //var status = await GattService.RequestAccessAsync();
                //if (status == DeviceAccessStatus.Allowed)
                //{

                //    var rett = await GattService.GetCharacteristicsForUuidAsync(dataSourceUuid);
                //    DataSource = new DataSource(rett.Characteristics[0]);

                //    rett = await GattService.GetCharacteristicsForUuidAsync(notificationSourceUuid);
                //    NotificationSource = new NotificationSource(rett.Characteristics[0]);
                //}


                DataSource.ApplicationAttributesReceived += DataSource_ApplicationAttributesReceived;
                DataSource.NotificationAttributesReceived += DataSource_NotificationAttributesReceived;
                NotificationSource.ValueChanged += NotificationSource_ValueChanged;



                try
                {
                    if (!subscribedForNotifications)
                    {
                        subscribedForNotifications = await NotificationSource.SubscribeAsync();
                        subscribedForNotifications &= await DataSource.SubscribeAsync();
                    }
                }
                catch (Exception e)
                {
                    if (subscribedForNotifications)
                    {
                        await NotificationSource.UnsubscribeAsync();
                        await DataSource.UnsubscribeAsync();
                    }

                    DataSource.ApplicationAttributesReceived -= DataSource_ApplicationAttributesReceived;
                    DataSource.NotificationAttributesReceived -= DataSource_NotificationAttributesReceived;
                    NotificationSource.ValueChanged -= NotificationSource_ValueChanged;
                    subscribedForNotifications = false;
                }
            }
            else
            {
                DataSource.ApplicationAttributesReceived -= DataSource_ApplicationAttributesReceived;
                DataSource.NotificationAttributesReceived -= DataSource_NotificationAttributesReceived;
                NotificationSource.ValueChanged -= NotificationSource_ValueChanged;
                subscribedForNotifications = false;
            }
        }

        public async void OnToastNotificationReceived(IActivatedEventArgs e)
        {
            // Handle toast activation
            if (e is ToastNotificationActivatedEventArgs)
            {
                var toastActivationArgs = e as ToastNotificationActivatedEventArgs;

                // Parse the query string
                QueryString args = QueryString.Parse(toastActivationArgs.Argument);

                // See what action is being requested 
                switch (args["action"])
                {
                    case "positive":
                        await ControlPoint.PerformNotificationActionAsync(Convert.ToUInt32(args["uid"]), ActionID.Positive);
                        break;
                    case "negative":
                        await ControlPoint.PerformNotificationActionAsync(Convert.ToUInt32(args["uid"]), ActionID.Negative);
                        break;
                }
            }
        }

        private void RaiseNotificationEvent(NotificationAttributeCollection attributes)
        {
            NotificationSourceData sourceData = Notifications[attributes.NotificationUID];

            switch (sourceData.EventId)
            {
                case EventID.NotificationAdded:
                    NotificationAdded?.Invoke(this, new AppleNotificationEventArgs(sourceData, attributes));
                    break;
                case EventID.NotificationModified:
                    NotificationModified?.Invoke(this, new AppleNotificationEventArgs(sourceData, attributes));
                    break;
                case EventID.NotificationRemoved:
                    // Has been handled, but just in case..
                    NotificationRemoved?.Invoke(this, sourceData);
                    break;
            }

            // Remove the notification from the list
            Notifications.Remove(sourceData.NotificationUID);
        }

        private void DataSource_ApplicationAttributesReceived(ApplicationAttributeCollection obj)
        {
            if (Applications.ContainsKey(obj.AppIdentifier))
            {
                Applications[obj.AppIdentifier] = obj;
            }
            else
            {
                Applications.Add(obj.AppIdentifier, obj);
            }

            if (ApplicationNotificationQueue.ContainsKey(obj.AppIdentifier))
            {
                Queue<NotificationAttributeCollection> queue = ApplicationNotificationQueue[obj.AppIdentifier];
                while (queue.Count > 0)
                {
                    RaiseNotificationEvent(queue.Dequeue());
                }

                ApplicationNotificationQueue.Remove(obj.AppIdentifier);
            }
        }

        private async void DataSource_NotificationAttributesReceived(NotificationAttributeCollection attributes)
        {
            // Is it a known notification?
            if (Notifications.ContainsKey(attributes.NotificationUID) == false)
            {
                return;
            }

            ApplicationAttributeCollection applicationAttributes;

            if (attributes.ContainsKey(NotificationAttributeID.AppIdentifier))
            {
                string appIdentifier = attributes[NotificationAttributeID.AppIdentifier];

                if (Applications.ContainsKey(appIdentifier) == false)
                {
                    // Enque notifications
                    if (ApplicationNotificationQueue.ContainsKey(appIdentifier) == false)
                    {
                        ApplicationNotificationQueue.Add(appIdentifier, new Queue<NotificationAttributeCollection>());
                    }
                    ApplicationNotificationQueue[appIdentifier].Enqueue(attributes);

                    List<AppAttributeID> requestAppAttributes = new List<AppAttributeID>();
                    requestAppAttributes.Add(AppAttributeID.DisplayName);

                    try
                    {
                        var commStatus = await ControlPoint.GetAppAttributesAsync(attributes[NotificationAttributeID.AppIdentifier], requestAppAttributes);
                    }
                    catch (Exception e)
                    {
                        System.Diagnostics.Debug.WriteLine("Bad get app attributes request");
                    }
                    return;
                }

                applicationAttributes = Applications[appIdentifier];
            }

            RaiseNotificationEvent(attributes);
        }

        /// <summary>
        /// When the value is changed a new notification ha arrived. We need to send a query about notification
        /// to the ControlPoint to get the actual notification message.
        /// </summary>
        /// <param name="obj"></param>
        private async void NotificationSource_ValueChanged(NotificationSourceData obj)
        {
            // TODO: Check this out. Not sure why but sometime I get a Notification UID = 0
            //       which breaks everything.
            if (obj.NotificationUID == 0)
            {
                return;
            }

            // We don't care about old notifications
            if (obj.EventFlags.HasFlag(EventFlags.EventFlagPreExisting))
            {
                return;
            }

            // Remove notifications don't need more data
            if (obj.EventId == EventID.NotificationRemoved)
            {
                if (Notifications.ContainsKey(obj.NotificationUID))
                {
                    Notifications.Remove(obj.NotificationUID);
                }

                NotificationRemoved?.Invoke(this, obj);
                return;
            }

            // Store the notification
            if (Notifications.ContainsKey(obj.NotificationUID))
            {
                Notifications[obj.NotificationUID] = obj;
            }
            else
            {
                Notifications.Add(obj.NotificationUID, obj);
            }

            // Build the attributes list for the GetNotificationAttributtes command.   
            List<NotificationAttributeID> attributes = new List<NotificationAttributeID>();
            attributes.Add(NotificationAttributeID.AppIdentifier);
            attributes.Add(NotificationAttributeID.Title);
            attributes.Add(NotificationAttributeID.Message);

            if (obj.EventFlags.HasFlag(EventFlags.EventFlagPositiveAction))
            {
                attributes.Add(NotificationAttributeID.PositiveActionLabel);
            }

            if (obj.EventFlags.HasFlag(EventFlags.EventFlagNegativeAction))
            {
                attributes.Add(NotificationAttributeID.NegativeActionLabel);
            }

            try
            {
                var communicationStatus = await ControlPoint.GetNotificationAttributesAsync(obj.NotificationUID, attributes);
            }
            catch (Exception ex)
            {
                // Simply log the exception to output console
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }
    }
}
