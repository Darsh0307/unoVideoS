using System;
using Uno.Devices.Midi.Internal;
using Windows.Storage.Streams;

namespace Windows.Devices.Midi
{
	/// <summary>
	/// Represents a MIDI message that specifies a control change.
	/// </summary>
	public partial class MidiControlChangeMessage : IMidiMessage
	{
		private readonly InMemoryBuffer _buffer;

		/// <summary>
		/// Creates a new MidiControlChangeMessage object.
		/// </summary>
		/// <param name="channel">The channel from 0-15 that this message applies to.</param>
		/// <param name="controller">The controller from 0-127 to receive this message.</param>
		/// <param name="controlValue">The value from 0-127 to apply to the controller.</param>
		public MidiControlChangeMessage(byte channel, byte controller, byte controlValue)
		{
			MidiMessageValidators.VerifyRange(channel, MidiMessageParameter.Channel);
			MidiMessageValidators.VerifyRange(controller, MidiMessageParameter.Controller);
			MidiMessageValidators.VerifyRange(controlValue, MidiMessageParameter.ControlValue);

			_buffer = new InMemoryBuffer(new byte[]
			{
				(byte)((byte)MidiMessageType.ControlChange | channel),
				controller,
				controlValue
			});
		}

		internal MidiControlChangeMessage(byte[] rawData)
		{
			MidiMessageValidators.VerifyMessageLength(rawData, 3, MidiMessageType.ControlChange);
			MidiMessageValidators.VerifyMessageType(rawData[0], MidiMessageType.ControlChange);
			MidiMessageValidators.VerifyRange(MidiHelpers.GetChannel(rawData[0]), MidiMessageParameter.Channel);
			MidiMessageValidators.VerifyRange(rawData[1], MidiMessageParameter.Controller);
			MidiMessageValidators.VerifyRange(rawData[2], MidiMessageParameter.ControlValue);

			_buffer = new InMemoryBuffer(rawData);
		}

		/// <summary>
		/// Gets the type of this MIDI message.
		/// </summary>
		public MidiMessageType Type => MidiMessageType.ControlChange;

		/// <summary>
		/// Gets the channel from 0-15 that this message applies to.
		/// </summary>
		public byte Channel => MidiHelpers.GetChannel(_buffer.Data[0]);

		/// <summary>
		/// Gets the value from 0-127 to apply to the controller.
		/// </summary>
		public byte ControlValue => _buffer.Data[2];

		/// <summary>
		/// Gets controller from 0-127 to receive this message.
		/// </summary>
		public byte Controller => _buffer.Data[1];

		/// <summary>
		/// Gets the array of bytes associated with the MIDI message, including status byte.
		/// </summary>
		public IBuffer RawData => _buffer;

		/// <summary>
		/// Gets the duration from when the MidiInPort was created to the time the message was received.
		/// For messages being sent to a MidiOutPort, this value has no meaning.
		/// </summary>
		public TimeSpan Timestamp { get; internal set; } = TimeSpan.Zero;
	}
}
