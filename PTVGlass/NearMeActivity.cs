﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.Locations;
using GlassProgressBar;
using Android.Glass.App;

namespace PTVGlass
{
	[Activity]
	public class NearMeActivity : Activity, ILocationListener
	{
		LocationManager locationManager;
		TransportType transportType;

		protected override void OnCreate(Bundle bundle)
		{
			base.OnCreate(bundle);

			// get transport type from intent
			transportType = (TransportType)Enum.Parse(typeof(TransportType), Intent.GetStringExtra("type"), true);

			// Show loading screen
			SetContentView(Resource.Layout.LoadingScreen);
			var loadingText = FindViewById<TextView>(Resource.Id.loading_text);
			loadingText.SetText(Resource.String.getting_location); // set loading text
			var progressBar = FindViewById<SliderView>(Resource.Id.indeterm_slider);
			progressBar.StartIndeterminate(); // start indeterminate progress bar

			// set up Android location manager
			locationManager = GetSystemService(Context.LocationService) as LocationManager;
			Criteria locationCriteria = new Criteria()
			{
				Accuracy = Accuracy.Coarse, // we only need coarse location accuracy
				AltitudeRequired = false // we're catching the bus, not planes
			};

			// GDK documentation strictly indicates Glass uses dybamic location providers which means we must listen to all providers
			// https://developers.google.com/glass/develop/gdk/location-sensors/index
			IList<string> providers = locationManager.GetProviders(locationCriteria, true); 
			foreach (string provider in providers)
			{
				locationManager.RequestLocationUpdates(provider, 1000, 1, this); // provide updates at least every second
			}
		}

		public async void NearbyDepartures(Location location)
		{
			var ptvApi = new PtvApi(); // new PTV API service

			// Show loading screen
			SetContentView(Resource.Layout.LoadingScreen);
			var loadingText = FindViewById<TextView>(Resource.Id.loading_text);
			loadingText.SetText(Resource.String.getting_stopsnearyou); // set loading text
			var progressBar = FindViewById<SliderView>(Resource.Id.indeterm_slider);
			progressBar.StartIndeterminate(); // start indeterminate progress bar

			// try to call PTV API to get nearby stops
			List<Stop> stopsNearby;
			try{
				stopsNearby = await ptvApi.StopsNearby(location.Latitude, location.Longitude);
			}catch(Exception e){
				// show error card
				var errorCard = new Card(this);
				errorCard.SetText (e.ToString());
				errorCard.SetFootnote (Resource.String.error);
				SetContentView (errorCard.ToView ());
				return;
			}

			// depending on our mode of transport, we want different number of stops and error messages
			int stopLimit = 1;
			int noStopsNearby = Resource.String.no_stops_nearby;
			switch (transportType)
			{
				case TransportType.Bus:
					stopLimit = 3;
					noStopsNearby = Resource.String.no_bus_stops_nearby;
					break;
				case TransportType.Tram:
					stopLimit = 3;
					noStopsNearby = Resource.String.no_tram_stops_nearby;
					break;
				case TransportType.Train:
					stopLimit = 1;
					noStopsNearby = Resource.String.no_train_stops_nearby;
					break;
			}

			// Cull our stops nearby to just how many we want
			stopsNearby = stopsNearby.Where(x =>
				x.TransportType == transportType
			).Take(stopLimit).ToList();

			// if there are no stops nearby, show no stops message
			if (stopsNearby.Count == 0)
			{
				var noStopsCard = new Card(this);
				noStopsCard.SetText(noStopsNearby);
				SetContentView(noStopsCard.ToView());
				return;
			}

			// Update loading text
			loadingText.SetText(Resource.String.getting_departures);

			// Get departures for each stop
			List<Departure> nearByDepartures = new List<Departure>();
			foreach (Stop stop in stopsNearby)
			{
				nearByDepartures.AddRange(await ptvApi.StationDepartures(stop.StopID, transportType, 1)); // merge departures together
			}

			// if there are no departures, show no departure message
			if (nearByDepartures.Count == 0)
			{
				var noDeparturesCard = new Card(this);
				noDeparturesCard.SetText(Resource.String.no_upcoming_departures);
				SetContentView(noDeparturesCard.ToView());
				return;
			}

			// show departures list screen
			ListView listView;
			SetContentView(Resource.Layout.DepartureScreen);
			listView = FindViewById<ListView>(Resource.Id.listview);
			// get the right type of screen adapter for the right type of transport
			if (transportType == TransportType.Train)
			{
				// we don't need the train "number" for nearby trains
				listView.Adapter = new NearbyTrainScreenAdapter(this, nearByDepartures); // bind list of station departures to listView
			}
			else
			{
				listView.Adapter = new NearbyBusTramScreenAdapter(this, nearByDepartures); // bind list of station departures to listView
			}
			listView.RequestFocus(); // set focus on the listView so scrolling works on the list
		}

		public void OnProviderEnabled(string provider)
		{
		}

		public void OnProviderDisabled(string provider)
		{
		}

		public void OnStatusChanged(string provider, Availability status, Bundle extras)
		{
		}

		public void OnLocationChanged(Location location)
		{
			// if location is within 500 meter accuracy, we'll accept it
			if (location.Accuracy < 500)
			{
				locationManager.RemoveUpdates(this); // stop getting location updates to save battery
				NearbyDepartures(location); // use the last known location to get nearby departures
			}
		}
	}

	public class NearbyBusTramScreenAdapter : BaseAdapter<Departure>
	{
		List<Departure> departures;
		Activity context;
		public NearbyBusTramScreenAdapter(Activity context, List<Departure> departures)
			: base()
		{
			this.context = context;
			this.departures = departures;
		}
		public override long GetItemId(int position)
		{
			return position;
		}
		public override Departure this[int position]
		{
			get { return departures[position]; }
		}
		public override int Count
		{
			get { return departures.Count; }
		}
		public override View GetView(int position, View convertView, ViewGroup parent)
		{
			var item = departures[position];
			View view = convertView;
			if (view == null) // no view to re-use, create new
				view = context.LayoutInflater.Inflate(Resource.Layout.BusTramDepartureWithStationRow, null);
			view.FindViewById<TextView>(Resource.Id.TextLineNum).Text = item.Platform.Direction.Line.LineNumber;
			view.FindViewById<TextView>(Resource.Id.TextLine).Text = item.Platform.Direction.DirectionName;
			view.FindViewById<TextView>(Resource.Id.TextTime).Text = TimeHelper.GetRelativeTime(item.TimeTimetableUTC);
			view.FindViewById<TextView>(Resource.Id.TextStop).Text = item.Platform.Stop.LocationName;
			return view;
		}
	}

	public class NearbyTrainScreenAdapter : BaseAdapter<Departure>
	{
		List<Departure> departures;
		Activity context;
		public NearbyTrainScreenAdapter(Activity context, List<Departure> departures)
			: base()
		{
			this.context = context;
			this.departures = departures;
		}
		public override long GetItemId(int position)
		{
			return position;
		}
		public override Departure this[int position]
		{
			get { return departures[position]; }
		}
		public override int Count
		{
			get { return departures.Count; }
		}
		public override View GetView(int position, View convertView, ViewGroup parent)
		{
			var item = departures[position];
			View view = convertView;
			if (view == null) // no view to re-use, create new
				view = context.LayoutInflater.Inflate(Resource.Layout.TrainDepartureWithStationRow, null);
			view.FindViewById<TextView>(Resource.Id.TextLine).Text = item.Platform.Direction.DirectionName;
			view.FindViewById<TextView>(Resource.Id.TextTime).Text = TimeHelper.GetRelativeTime(item.TimeTimetableUTC);
			view.FindViewById<TextView>(Resource.Id.TextStop).Text = item.Platform.Stop.LocationName;
			return view;
		}
	}
}

