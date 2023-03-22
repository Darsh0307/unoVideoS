﻿using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Net.Http;
using Android.Webkit;
using Android.Views;
using Android.Content;
using Uno.Extensions;
using Windows.Foundation;
using System.Collections.Generic;

namespace Uno.UI.Xaml.Controls;

internal class NativeWebViewWrapper : INativeWebView
{
	private readonly WebView _webView;
	private bool _wasLoadedFromString;

	public NativeWebViewWrapper(WebView webView)
	{
		_webView = webView;

		// For some reason, the native WebView requires this internal registration
		// to avoid launching an external task, out of context of the current activity.
		//
		// this will still be used to handle extra activity with the native control.

		_webView.SetWebViewClient(new InternalClient(this));
		_webView.SetWebChromeClient(new InternalWebChromeClient());
		_webView.Settings.JavaScriptEnabled = true;
		_webView.Settings.DomStorageEnabled = true;
		_webView.Settings.BuiltInZoomControls = true;
		_webView.Settings.DisplayZoomControls = false;
		_webView.Settings.SetSupportZoom(true);
		_webView.Settings.LoadWithOverviewMode = true;
		_webView.Settings.UseWideViewPort = true;

		//Allow ThirdPartyCookies by default only on Android 5.0 and UP
		if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Lollipop)
		{
			Android.Webkit.CookieManager.Instance.SetAcceptThirdPartyCookies(_webView, true);
		}

		// The native webview control requires to have LayoutParameters to function properly.
		_webView.LayoutParameters = new ViewGroup.LayoutParams(
			_webView.LayoutParams.MatchParent,
			_webView.LayoutParams.MatchParent);
	}

	internal WebView WebView => _webView;

	public void GoBack() => GoToNearestValidHistoryEntry(direction: -1 /* backward */);

	public void GoForward() => GoToNearestValidHistoryEntry(direction: 1 /* forward */);

	public void Stop() => _webView.StopLoading();

	public void Reload() => _webView.Reload();

	private void GoToNearestValidHistoryEntry(int direction) =>
		Enumerable
			.Repeat(
				element: direction > 0
					? (Action)_webView.GoForward
					: (Action)_webView.GoBack,
				count: GetStepsToNearestValidHistoryEntry(direction))
			.ForEach(action => action.Invoke());

	private int GetStepsToNearestValidHistoryEntry(int direction)
	{
		var history = _webView.CopyBackForwardList();

		// Iterate through every next/previous (depending on direction) history entry until a valid one is found
		for (int i = history.CurrentIndex + direction; 0 <= i && i < history.Size; i += direction)
			if (GetIsHistoryEntryValid(history.GetItemAtIndex(i).Url))
				// return the absolute number of steps from the current entry to the nearest valid entry
				return Math.Abs(i - history.CurrentIndex);

		return 0; // no valid entry found
	}

	internal bool GetIsHistoryEntryValid(string url) =>
		!url.IsNullOrWhiteSpace() &&
		!url.Equals(BlankUrl, StringComparison.OrdinalIgnoreCase);

	internal void NavigatePartial(Uri uri)
	{
		if (!VerifyWebViewAvailability())
		{
			return;
		}

		_wasLoadedFromString = false;
		if (uri.Scheme.Equals("local", StringComparison.OrdinalIgnoreCase))
		{
			var path = $"file:///android_asset/{uri.PathAndQuery}";
			_webView.LoadUrl(path);
			return;
		}

		if (uri.Scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase))
		{
			CreateAndLaunchMailtoIntent(_webView.Context, uri.AbsoluteUri);
			return;
		}

		//The replace is present because the uri cuts off any slashes that are more than two when it creates the uri.
		//Therefore we add the final forward slash manually in Android because the file:/// requires 3 slashles.
		_webView.LoadUrl(uri.AbsoluteUri.Replace("file://", "file:///"));
	}


	private void NavigateWithHttpRequestMessagePartial(HttpRequestMessage requestMessage)
	{
		if (!VerifyWebViewAvailability())
		{
			return;
		}

		var uri = requestMessage.RequestUri;
		var headers = requestMessage.Headers
			.Safe()
			.ToDictionary(
				header => header.Key,
				element => element.Value.JoinBy(", ")
			);

		_wasLoadedFromString = false;
		_webView.LoadUrl(uri.AbsoluteUri, headers);
	}

	private void NavigateToStringPartial(string text)
	{
		if (!VerifyWebViewAvailability())
		{
			return;
		}

		_wasLoadedFromString = true;
		//Note : _webView.LoadData does not work properly on Android 10 even when we encode to base64.
		_webView.LoadDataWithBaseURL(null, text, "text/html; charset=utf-8", "utf-8", null);
	}

	//_owner should be IAsyncOperation<string> instead of Task<string> but we use an extension method to enable the same signature in Win.
	//IAsyncOperation is not available in Xamarin.
	internal async Task<string> InvokeScriptAsync(CancellationToken ct, string script, string[] arguments)
	{
		var argumentString = ConcatenateJavascriptArguments(arguments);

		TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();
		ct.Register(() => tcs.TrySetCanceled());

		_webView.EvaluateJavascript(
			string.Format(CultureInfo.InvariantCulture, "javascript:{0}(\"{1}\");", script, argumentString),
			new ScriptResponse(value => tcs.SetResult(value)));

		return await tcs.Task;
	}

	internal IAsyncOperation<string> InvokeScriptAsync(string scriptName, IEnumerable<string> arguments) =>
		AsyncOperation.FromTask(ct => InvokeScriptAsync(ct, scriptName, arguments?.ToArray()));


	// On Windows, the WebView ignores "about:blank" entries from its navigation history.
	// Because Android doesn't let you modify the navigation history, 
	// we need CanGoBack, CanGoForward, GoBack and GoForward to take the above condition into consideration.

	private void OnNavigationHistoryChanged()
	{
		// A non-zero number of steps to the nearest valid history entry means that navigation in the given direction is allowed
		CanGoBack = GetStepsToNearestValidHistoryEntry(direction: -1 /* backward */) != 0;
		CanGoForward = GetStepsToNearestValidHistoryEntry(direction: 1 /* forward */) != 0;
	}

	private class ScriptResponse : Java.Lang.Object, IValueCallback
	{
		private Action<string> _setCallBackValue;

		internal ScriptResponse(Action<string> setCallBackValue)
		{
			_setCallBackValue = setCallBackValue;
		}

		public void OnReceiveValue(Java.Lang.Object value)
		{
			_setCallBackValue(value?.ToString() ?? string.Empty);
		}
	}

	private void OnScrollEnabledChangedPartial(bool scrollingEnabled)
	{
		_webView.HorizontalScrollBarEnabled = scrollingEnabled;
		_webView.VerticalScrollBarEnabled = scrollingEnabled;
	}

	internal void OnNewWindowRequested(WebViewNewWindowRequestedEventArgs args)
	{
		NewWindowRequested?.Invoke(_owner, args);
	}

	private bool VerifyWebViewAvailability()
	{
		if (_webView == null)
		{
			if (_isLoaded)
			{
				_owner.Log().Warn("_owner WebView control instance does not have a UIViewView child, a Control template may be missing.");
			}

			return false;
		}

		return true;
	}
}

