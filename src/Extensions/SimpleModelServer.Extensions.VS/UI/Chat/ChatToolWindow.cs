using System;
using Microsoft.VisualStudio.Extensibility;
using SimpleModelServer.UI.Core;

namespace SimpleModelServer.Extensions.VS.UI.Chat
{
	/// <summary>
	/// Chat Tool Window Provider for the VS extension.
	/// This provides the chat tool window to Visual Studio.
	/// </summary>
	[VisualStudioContribution]
	internal class ChatToolWindow
	{
		private readonly ChatService _chatService;
		private readonly SettingsManager _settingsManager;

		public ChatToolWindow(ChatService chatService, SettingsManager settingsManager)
		{
			_chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
			_settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
		}

		/// <summary>
		/// Gets the chat tool window control.
		/// </summary>
		public ChatToolWindowControl GetControl()
		{
			return new ChatToolWindowControl();
		}
	}
}
