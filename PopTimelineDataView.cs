using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UnityEditor;



[System.Serializable]
public class UnityEvent_TimeUnit : UnityEngine.Events.UnityEvent <PopTimeline.TimeUnit> {}




namespace PopTimeline
{
	//	typed so we can change it later
	public struct TimeUnit
	{
		public int		Time;

		public TimeUnit (int TimeValue)
		{
			this.Time = TimeValue;
		}

		public string	GetLabel(bool WithRawMs=false)	
		{
			var Ms = Time % 1000;
			var Secs = (Time / 1000) % 60;
			var Mins = (Time / 1000 / 60) % 60;
			var Hours = (Time / 1000 / 60 / 60);

			var TimeString = "";
			TimeString = TimeString.Insert (0, Ms.ToString ("D3") + " ms" );
			TimeString = TimeString.Insert (0, Secs.ToString("D2") + "." );

			//	only display as much as we need to
			if (Mins > 0 || Hours > 0) {
				TimeString = TimeString.Insert (0, Mins.ToString ("D2") + ":" );
				if (Hours > 0) {
					TimeString = TimeString.Insert (0, Hours.ToString ("D2") + ":" );
				}
			}

			if ( WithRawMs )
			{
				TimeString += " (" + Time + ")";
			}

			return TimeString;
		}
	}
}


namespace PopTimeline
{
	public delegate void	EnumCommand(string Label,System.Action Callback);

	//	stream meta
	public class DataStreamMeta
	{
		public string	Name;
		public Color 	Colour = Color.red;
		public bool		Draggable { get { return OnDragged != null; } }
		public System.Action<TimeUnit, TimeUnit> OnDragged = null;
		public System.Action<TimeUnit, EnumCommand> OnCreateContextMenu = null;

		public DataStreamMeta(string Name)
		{
			this.Name = Name;
		}
	}

	//	may want something more abstract here
	//	reader -> exists, loading, loaded
	//	writer -> exists, writing, written
	//	streamer -> exists, downloading, writing, written
	public enum DataState
	{
		Exists,		//	exists but not loaded
		Loaded,		//	data is ready
	};

	public interface StreamDataItem
	{
		TimeUnit			GetStartTime();
		TimeUnit			GetEndTime();
		DataState			GetStatus();
	}

	public abstract class DataBridge
	{
		public abstract List<DataStreamMeta>	Streams	{	get; }	

		public abstract List<StreamDataItem>	GetStreamData(DataStreamMeta StreamMeta,TimeUnit MinTime,TimeUnit MaxTime);
		public abstract StreamDataItem			GetNearestOrPrevStreamData(DataStreamMeta StreamMeta, ref TimeUnit Time);
		public abstract StreamDataItem			GetNearestOrNextStreamData(DataStreamMeta StreamMeta, ref TimeUnit Time);
		public abstract int						GetDataCount (DataStreamMeta StreamMeta);
		public abstract void					GetTimeRange (out PopTimeline.TimeUnit Min, out PopTimeline.TimeUnit Max);
	}
}


namespace PopTimeline
{
	//	abstracted from untiy GUI so later we can do a in-game viewer
	public class DataView
	{
		readonly Color CanvasBackgroundColour = new Color (0.1f, 0.1f, 0.1f);
		readonly Color StreamBackgroundColour = new Color (0.3f, 0.3f, 0.3f);
		readonly Color BlockNotchColour = new Color(0.3f, 0.3f, 0.3f);
		readonly Color SelectionColour = new Color(1.0f, 1.0f, 1.0f, 0.5f);
		readonly Color HoverColour = new Color (0.1f, 0.1f, 0.1f);
		readonly Color DragColour = new Color(0.9f, 0.9f, 0.9f, 0.3f);
		readonly Color StreamLabelColour = new Color (1.0f, 1.0f, 1.0f, 0.8f);
		readonly FontStyle StreamLabelFontStyle = FontStyle.Bold;
		readonly Color TimeLabelColour = new Color (1.0f, 1.0f, 1.0f, 1.0f);
		readonly FontStyle TimeLabelFontStyle = FontStyle.Bold;
		readonly Color SelectedTimeLabelColour = new Color (1.0f, 1.0f, 1.0f, 1.0f);
		readonly FontStyle SelectedTimeLabelFontStyle = FontStyle.Bold;

		//	in case something goes wrong
		const int MaxDataDraws = 9000;

		float GetTimeNormalised(TimeUnit Min,TimeUnit Max,TimeUnit Value)
		{
			var Norm = PopMath.Range (Min.Time, Max.Time, Value.Time);
			//	gr: clip here?
			return Norm;
		}

		public void Draw(Rect CanvasRect,TimeUnit LeftTime,TimeUnit RightTime,TimeUnit? SelectedTime,TimeUnit? HoverTime,DataBridge Data,List<DataStreamMeta> Streams,DragMeta DragMeta)
		{
			EditorGUI.DrawRect (CanvasRect, CanvasBackgroundColour);
			//DrawWholeView (Canvas, LeftTime, RightTime, Data);

			var StreamRects = new Rect[Streams.Count];
			for (int s = 0;	s < Streams.Count;	s++) {

				var st0 = (s+0) / (float)Streams.Count;
				var st1 = (s+1) / (float)Streams.Count;

				var StreamBorder = 1;
				var Top = Mathf.Lerp (CanvasRect.min.y + StreamBorder, CanvasRect.max.y, st0);
				var Bot = Mathf.Lerp (CanvasRect.min.y + StreamBorder, CanvasRect.max.y, st1) - StreamBorder;
				var Left = CanvasRect.min.x + StreamBorder;
				var Right = CanvasRect.max.x - StreamBorder;
				var StreamRect = new Rect (Left, Top, Right - Left, Bot - Top);
				StreamRects [s] = StreamRect;
			}

			var DrawCap = MaxDataDraws;
			Rect? FirstSelectionRect = null;

			//	draw streams
			for (int s = 0;	s < Streams.Count;	s++) {
				var StreamRect = StreamRects [s];
				var Stream = Streams [s];
				EditorGUI.DrawRect (StreamRect, StreamBackgroundColour);
				var StreamColour = Stream.Colour;

				//	get all the data in the visible region
				var StreamDatas = Data.GetStreamData( Stream, LeftTime, RightTime );
				var StreamDataRect = new Rect (0, 0, 1, 1);
				var MinWidthPx = 1;

				System.Func<TimeUnit, TimeUnit, Color, DataState,Rect> DrawMarker = (DataTimeLeft, DataTimeRight, Colour, State) =>
				{
					var LeftNorm = GetTimeNormalised(LeftTime, RightTime, DataTimeLeft);
					var RightNorm = GetTimeNormalised(LeftTime, RightTime, DataTimeRight);
					StreamDataRect.x = LeftNorm;
					StreamDataRect.width = RightNorm - LeftNorm;
					var DrawStreamDataRect = PopMath.RectMult(StreamDataRect, StreamRect);

					DrawStreamDataRect.width = Mathf.Max(MinWidthPx, DrawStreamDataRect.width);
					if (DrawStreamDataRect.width <= 2.0f)
						DrawStreamDataRect.width = MinWidthPx;

					if (State == DataState.Loaded)
					{
						EditorGUI.DrawRect(DrawStreamDataRect, Colour);
					}
					else
					{
						var StripeHeight = 2;
						int i = 0;
						var yoffset = (DrawStreamDataRect.height % StripeHeight) - (StripeHeight/2.0f);
						for (var y=0; y < DrawStreamDataRect.height+StripeHeight;	y+=StripeHeight,i++ )
						{
							if (i % 3 == 2)
								continue;
							var Rect = DrawStreamDataRect;
							Rect.y = y + yoffset + DrawStreamDataRect.yMin;
							Rect.height = StripeHeight;
							//	clip
							if (Rect.yMin > DrawStreamDataRect.yMax)
								continue;
							Rect.yMin = Mathf.Max(Rect.yMin, DrawStreamDataRect.yMin);
							Rect.yMax = Mathf.Min(Rect.yMax, DrawStreamDataRect.yMax);
							EditorGUI.DrawRect(Rect, Colour);
						}

					}

					return DrawStreamDataRect;
				};

				System.Func<TimeUnit,TimeUnit,Color,DataState,Rect> DrawData = (DataTimeLeft, DataTimeRight, Colour, State) => 
				{
					var DrawStreamDataRect = DrawMarker(DataTimeLeft, DataTimeRight, Colour, State);

					//	put some notches in long data
					var DurationMs = DataTimeRight.Time - DataTimeLeft.Time;
					for (int NotchMs = 1000; NotchMs < DurationMs;	NotchMs+=1000 )
					{
						var LeftNorm = GetTimeNormalised(LeftTime, RightTime, new TimeUnit(DataTimeLeft.Time+NotchMs));
						//var RightNorm = LeftNorm;
						StreamDataRect.x = LeftNorm;
						StreamDataRect.width = 0;
						var NotchDrawStreamDataRect = PopMath.RectMult(StreamDataRect, StreamRect);
						NotchDrawStreamDataRect.width = MinWidthPx;
						EditorGUI.DrawRect(NotchDrawStreamDataRect, BlockNotchColour);
					}

					//	change cursor if this is draggable
					if ( Stream.Draggable )
						EditorGUIUtility.AddCursorRect(DrawStreamDataRect, MouseCursor.Pan);

					return DrawStreamDataRect;
				};

				System.Func<TimeUnit, Color, Rect> DrawLine = (DataTimeLeft, Colour) =>
				{
					var SelectedTimeDuration = 16;
					var DataTimeRight = new TimeUnit(DataTimeLeft.Time + SelectedTimeDuration);
					return DrawMarker(DataTimeLeft, DataTimeRight, Colour, DataState.Loaded);
				};

				//	draw hover underneath
				if (HoverTime.HasValue) 
				{
					var SelectedTimeLeft = HoverTime.Value;
					DrawLine (SelectedTimeLeft, HoverColour);
				}


				foreach (var StreamData in StreamDatas) 
				{
					if (DrawCap-- <= 0)
						break;

					DrawData (StreamData.GetStartTime (), StreamData.GetEndTime (), StreamColour, StreamData.GetStatus());

					//	draw again offset by drag
					if ( DragMeta != null && DragMeta.Draggable && DragMeta.StreamIndex == s)
					{
						var DraggedStartTime = new TimeUnit(StreamData.GetStartTime().Time + DragMeta.DragAmount.Time);
						var DraggedEndTime = new TimeUnit(StreamData.GetEndTime().Time + DragMeta.DragAmount.Time);
						DrawData(DraggedStartTime, DraggedEndTime, DragColour, StreamData.GetStatus());
					}

				}

				//	draw selection over the top
				if (SelectedTime.HasValue) 
				{
					var SelectedTimeLeft = SelectedTime.Value;
					var Rect = DrawLine (SelectedTimeLeft, SelectionColour);
					if ( !FirstSelectionRect.HasValue )
						FirstSelectionRect = Rect;
				}

				//	draw text over that
				var LabelStyle = new GUIStyle();
				LabelStyle.alignment = TextAnchor.LowerLeft;
				LabelStyle.fontStyle = StreamLabelFontStyle;
				LabelStyle.normal.textColor = StreamLabelColour;
				EditorGUI.DropShadowLabel(StreamRect,Stream.Name,LabelStyle);
			}


			//	draw time labels
			{
				var LabelStyle = new GUIStyle ();
				LabelStyle.alignment = TextAnchor.UpperLeft;
				LabelStyle.fontStyle = TimeLabelFontStyle;
				LabelStyle.normal.textColor = TimeLabelColour;
				EditorGUI.DropShadowLabel (CanvasRect, "|< " + LeftTime.GetLabel(false), LabelStyle);
			}
			{
				var LabelStyle = new GUIStyle ();
				LabelStyle.alignment = TextAnchor.UpperRight;
				LabelStyle.fontStyle = TimeLabelFontStyle;
				LabelStyle.normal.textColor = TimeLabelColour;
				EditorGUI.DropShadowLabel (CanvasRect, RightTime.GetLabel(false) + " >|", LabelStyle);
			}
			if ( SelectedTime.HasValue && FirstSelectionRect.HasValue )
			{
				var LabelStyle = new GUIStyle ();
				LabelStyle.alignment = TextAnchor.UpperLeft;
				LabelStyle.fontStyle = SelectedTimeLabelFontStyle;
				LabelStyle.normal.textColor = SelectedTimeLabelColour;
				var Label = "\n<<" + SelectedTime.Value.GetLabel (true);
				EditorGUI.DropShadowLabel (FirstSelectionRect.Value, Label, LabelStyle);
			}

			if (DrawCap <= 0)
				Debug.Log("Exceeded draw cap");
		}


		public void DrawWholeView(Rect Canvas,TimeUnit LeftTime,TimeUnit RightTime,DataBridge Data)
		{
			TimeUnit MinTime, MaxTime;
			Data.GetTimeRange ( out MinTime, out MaxTime );

			var LeftNorm = GetTimeNormalised(MinTime, MaxTime, LeftTime);
			var RightNorm = GetTimeNormalised(MinTime, MaxTime, RightTime);

			var RectNorm = new Rect (LeftNorm, 0, RightNorm - LeftNorm, 1);
			var VisibleRect = PopMath.RectMult (RectNorm, Canvas);

			//EditorGUI.DrawRect (Canvas, CanvasBackgroundColour);
			EditorGUI.DrawRect (VisibleRect, Color.red);
		}
	}
}


namespace PopTimeline
{
	public class DragMeta
	{
		public int?				StreamIndex = null;
		public TimeUnit?		GrabTime;				//	if null then they started a drag in a bad place, but we keep this struct to stop accidentally triggering another
		public TimeUnit			DragAmount;
		public bool				Draggable = true;
	};

	public struct StreamAndTime
	{
		public DataStreamMeta Stream;
		public TimeUnit Time;

		public StreamAndTime(DataStreamMeta Stream,TimeUnit Time)
		{
			this.Stream = Stream;
			this.Time = Time;
		}
	};

	public class DataViewInput
	{
		//	nicer names please unity!
		public enum MouseButton
		{
			Left = 0,
			Right = 1,
			Middle = 2,
			Back = 3,
			Forward = 4
		}
		const MouseButton MouseButton_Select = MouseButton.Left;
		const MouseButton MouseButton_Drag = MouseButton.Middle;
		const MouseButton MouseButton_Menu = MouseButton.Right;
		const MouseButton MouseButton_JumpPrev = MouseButton.Back;
		const MouseButton MouseButton_JumpNext = MouseButton.Forward;


		//	event cache, waiting to be converted in OnGui
		const int MouseWheelScrollRateMs = 1000;
		Vector2? MouseSelectClickPos = null; //	this value is popped by UI
		Vector2? MouseSelectDownPos = null;  //	this value remains for other-ui-event-handling to see if mouse is still down
		Vector2? MouseHoverPos = null;

		public Vector2? MouseDragCurrentPosition = null;    //	new drag pos
		public Vector2? MouseDragEndPosition = null;        //	set when we've ended a drag

		Vector2? MouseMenuPos = null;        //	this value is popped by UI
		public Vector2? MouseJumpPrevPos = null;    //	this value is popped by UI
		public Vector2? MouseJumpNextPos = null;    //	this value is popped by UI

		public TimeUnit? ScrollDelta = null;

		public void HandleInput(EventType Event, Vector2 Position, MouseButton Button, Vector2 ScrollDelta)
		{
			switch (Event)
			{
				case EventType.MouseMove:
				case EventType.MouseEnterWindow:
					MouseHoverPos = Position;
					break;

				case EventType.MouseLeaveWindow:
					MouseHoverPos = null;
					break;

				case EventType.ScrollWheel:
					this.ScrollDelta = new TimeUnit((int)(MouseWheelScrollRateMs * ScrollDelta.y));
					//	as time changes, if mouse is held down, treat it like a re-click so we can hold mouse & scroll
					if (MouseSelectDownPos.HasValue)
					{
						MouseSelectClickPos = MouseSelectDownPos;
						MouseHoverPos = MouseSelectDownPos;
					}

					break;

				case EventType.MouseDown:
					if (Button == MouseButton_Select)
					{
						MouseSelectClickPos = Position;
						MouseSelectDownPos = Position;
					}
					else if (Button == MouseButton_Drag)
					{
						MouseDragCurrentPosition = Position;
					}
					else if (Button == MouseButton_Menu)
					{
						MouseMenuPos = Position;
						break;
					}
					else if (Button == MouseButton_JumpPrev)
					{
						MouseJumpPrevPos = Position;
						break;
					}
					else if (Button == MouseButton_JumpNext)
					{
						MouseJumpNextPos = Position;
						break;
					}
					break;

				case EventType.MouseDrag:
					if (Button == MouseButton_Select)
					{
						MouseSelectClickPos = Position;
						MouseSelectDownPos = Position;
					}
					else if (Button == MouseButton_Drag)
					{
						MouseDragCurrentPosition = Position;
					}
					break;

				case EventType.MouseUp:
					MouseHoverPos = Position;
					if (Button == MouseButton_Select)
					{
						MouseSelectDownPos = null;
					}
					else if (Button == MouseButton_Drag)
					{
						MouseDragEndPosition = Position;
					}
					break;
			}
		}
	

		public void ProcessScroll(System.Action<TimeUnit> OnScroll)
		{
			if (!ScrollDelta.HasValue)
				return;
			try
			{
				var Scroll = ScrollDelta.Value;
				ScrollDelta = null;
				OnScroll.Invoke(Scroll);
			}
			catch(System.Exception e)
			{
				Debug.LogException(e);
			}
		}

		public void ProcessSelect(System.Func<Vector2,TimeUnit?> PositionToTime,System.Action<TimeUnit?> OnClick)
		{
			if (!MouseSelectClickPos.HasValue)
				return;
			
			var NewSelectionTime = PositionToTime(MouseSelectClickPos.Value);
			try
			{
				OnClick(NewSelectionTime);
			}
			catch(System.Exception e)
			{
				Debug.LogException(e);
			}
			MouseSelectClickPos = null;
		}

		public void ProcessHover(System.Func<Vector2, TimeUnit?> PositionToTime,System.Action<TimeUnit?> OnHover)
		{
			if (!MouseHoverPos.HasValue)
				return;

			var HoverTime = PositionToTime( MouseHoverPos.Value);
			try
			{
				OnHover.Invoke(HoverTime);
			}
			catch(System.Exception e)
			{
				Debug.LogException(e);
			}
			//	leave last known pos
			//MouseHoverPos = null;
		}



		public void ProcessMenuClick(System.Func<Vector2,StreamAndTime> PositionToTime, System.Action<StreamAndTime> OnClick)
		{
			if (!MouseMenuPos.HasValue)
				return;

			try
			{
				var sat = PositionToTime( MouseMenuPos.Value );
				OnClick.Invoke(sat);
			}
			catch (System.Exception e)
			{
				Debug.LogException(e);
			}

			MouseMenuPos = null;
		}
	};

	public class DataViewWindow : EditorWindow 
	{
		DataViewInput		Input;

		DataBridge			Bridge;
		System.Exception	Error;
		DataView			View;

		TimeUnit?			SelectedTime = null;
		TimeUnit?			HoverTime = null;
		TimeUnit			ScrollTimeLeft;
		TimeUnit			ScrollVisibleTimeRange	{	get	{ return new TimeUnit (1000 * 10); }}
		TimeUnit			ScrollTimeRight			{	get { return new TimeUnit (ScrollTimeLeft.Time + ScrollVisibleTimeRange.Time); } }
		bool				StickyScroll = true;   //	gr: see if we can detect when there's new right max time and scroll if we were already at the edge
		bool				StickySelect			{ get { return LastStickySelectTime.HasValue; } }
		TimeUnit?			LastStickySelectTime = null;
		DragMeta			CurrentDrag;

		public UnityEvent_TimeUnit	OnTimeSelected;
		public UnityEvent_TimeUnit	OnTimeHover;


		public DataViewWindow()
		{
			if ( OnTimeSelected == null )
				OnTimeSelected = new UnityEvent_TimeUnit();
			if ( OnTimeHover == null )
				OnTimeHover = new UnityEvent_TimeUnit();

			wantsMouseMove = true;

			Input = new DataViewInput();
		}

		void OnInteractionEvent(SceneView sceneView)
		{
			var CurrentEvent = Event.current;
			Input.HandleInput(CurrentEvent.type, CurrentEvent.mousePosition, (DataViewInput.MouseButton)CurrentEvent.button, CurrentEvent.delta );
		}

		public void SetBridge(DataBridge Bridge)
		{			
			this.Bridge = Bridge;
		
			if (this.Bridge == null)
				Error = new System.Exception ("Null bridge");

			this.Repaint ();
		}

		//	todo: this is a quick bodge
		//	gr: this either needs to be done by the renderer, or we change it so the renderer is told explicitly to render each stream in it's own canvas
		//		(especially once we start having vertical scrolling)
		int? GetStreamIndexOnCanvas(float YNormalised)
		{
			var Streams = Bridge.Streams;
			var StreamCount = Bridge.Streams.Count;
			var StreamIndex = (int)(YNormalised * (float)StreamCount);
			if (StreamIndex < 0 || StreamIndex > StreamCount)
				return null;
			return StreamIndex;
		}


		const int FloatTimeScalar = 16 * 1000;
		float TimeUnitToFloat(TimeUnit Unit)
		{
			float t = Unit.Time / (float)(FloatTimeScalar);
			return t;
		}

		TimeUnit FloatToTimeUnit(float Time)
		{
			//	try and avoid float errors
			var t = (int)( Time * FloatTimeScalar );
			return new TimeUnit (t);
		}

		TimeUnit? PositionToTime(Rect Canvas, Vector2 Position, out float NormalisedY)
		{
			var xf = PopMath.Range(Canvas.xMin, Canvas.xMax, Position.x);
			var yf = PopMath.Range(Canvas.yMin, Canvas.yMax, Position.y);
			NormalisedY = yf;
			if (xf < 0 || xf > 1 || yf < 0 || yf > 1)
			{
				//Debug.Log ("xf = " + xf + " yf = " + yf);
				return null;
			}

			//	convert to time
			var MinTime = ScrollTimeLeft.Time;
			var MaxTime = ScrollTimeRight.Time;
			var Timef = Mathf.Lerp(MinTime, MaxTime, xf);
			var TimeMs = (int)Timef;

			return new TimeUnit(TimeMs);
		}

		TimeUnit? PositionToTime(Rect Canvas, Vector2 Position,List<DataStreamMeta> Streams,out int? StreamIndex)
		{
			var xf = PopMath.Range(Canvas.xMin, Canvas.xMax, Position.x);
			var yf = PopMath.Range(Canvas.yMin, Canvas.yMax, Position.y);
			if (xf < 0 || xf > 1 || yf < 0 || yf > 1)
			{
				//Debug.Log ("xf = " + xf + " yf = " + yf);
				StreamIndex = null;
				return null;
			}

			//	convert to time
			var MinTime = ScrollTimeLeft.Time;
			var MaxTime = ScrollTimeRight.Time;
			var Timef = Mathf.Lerp(MinTime, MaxTime, xf);
			var TimeMs = (int)Timef;

			//	work out which stream this is
			StreamIndex = GetStreamIndexOnCanvas(yf);

			return new TimeUnit(TimeMs);
		}

		TimeUnit? PositionToTime(Rect Canvas,Vector2 Position)
		{
			int? StreamIndex;
			return PositionToTime(Canvas, Position, null, out StreamIndex);
		}



		void OnMouseClick(TimeUnit MouseTime)
		{
			SelectedTime = MouseTime;
			try {
				OnTimeSelected.Invoke (SelectedTime.Value);
			} catch (System.Exception e) {
				Debug.LogException (e);
			}
			Repaint ();
		}

		void OnMouseHover(TimeUnit? MouseTime)
		{
			if (HoverTime.HasValue) 
			{
				Repaint ();
				try
				{
					OnTimeHover.Invoke (HoverTime.Value);
				} catch (System.Exception e) {
					Debug.LogException (e);
				}
			}
			Repaint ();
		}

		void OnSelectTime(TimeUnit Time)
		{
			OnMouseClick(Time);
		}

		void HandlePendingEvents(Rect Canvas,List<DataStreamMeta> Streams)
		{
			System.Func<Vector2, TimeUnit?> CanvasPositionToTime = (Position) =>
			{
				return PositionToTime(Canvas, Position);
			};

			System.Func<Vector2,StreamAndTime> CanvasPositionToStreamAndTime = (Position) =>
			{
				int? StreamIndex;
				var Time = PositionToTime(Canvas, Position, Streams, out StreamIndex);
				return new StreamAndTime( Streams[StreamIndex.Value], Time.Value );
			};

			Input.ProcessScroll((ScrollTime) =>
			{
				ScrollTimeLeft.Time += ScrollTime.Time;
				StickyScroll = false;
			});

			Input.ProcessSelect( CanvasPositionToTime, (SelectTime) =>
			{
				LastStickySelectTime = null;
				if ( SelectTime.HasValue )
					OnSelectTime(SelectTime.Value);
			});

			Input.ProcessHover( CanvasPositionToTime, (HoverTime) =>
			{
				OnMouseHover(HoverTime);
			});


			if (Input.MouseDragCurrentPosition.HasValue)
			{
				//	new drag
				if (CurrentDrag == null)
				{
					CurrentDrag = new DragMeta();
					CurrentDrag.DragAmount = new TimeUnit(0);

					CurrentDrag.GrabTime = PositionToTime(Canvas, Input.MouseDragCurrentPosition.Value, Streams, out CurrentDrag.StreamIndex);

					//	check is draggable
					CurrentDrag.Draggable = false;
					try
					{
						var Stream = Streams[CurrentDrag.StreamIndex.Value];
						CurrentDrag.Draggable = Stream.Draggable;
					}
					catch
					{
						CurrentDrag.Draggable = false;
					}
				}
				else
				{
					if ( CurrentDrag.Draggable )
					{
						//	update drag
						var NewDragTime = PositionToTime(Canvas, Input.MouseDragCurrentPosition.Value);
						if (CurrentDrag.GrabTime.HasValue && NewDragTime.HasValue)
						{
							CurrentDrag.DragAmount = new TimeUnit(NewDragTime.Value.Time - CurrentDrag.GrabTime.Value.Time);
						}
					}
				}
				Input.MouseDragCurrentPosition = null;
			}

			//	drag released 
			if ( Input.MouseDragEndPosition.HasValue)
			{
				//	invoke the drag
				try
				{
					var Stream = Streams[CurrentDrag.StreamIndex.Value];
					if (Stream.OnDragged != null )
						Stream.OnDragged(CurrentDrag.GrabTime.Value, CurrentDrag.DragAmount);
				}
				catch(System.Exception e)
				{
					Debug.LogException(e);
				}
				CurrentDrag = null;
				Input.MouseDragEndPosition = null;
			}

			if ( Input.MouseJumpPrevPos.HasValue )
			{
				try
				{
					int? StreamIndex;
					var JumpFromTime = PositionToTime(Canvas, Input.MouseJumpPrevPos.Value, Streams, out StreamIndex);
					//	get prev pos in stream
					var PrevItemTime = new TimeUnit(JumpFromTime.Value.Time - 1);
					var PrevItem = Bridge.GetNearestOrPrevStreamData(Streams[StreamIndex.Value], ref PrevItemTime );
				
					//	want the item to appear under mouse (where we clicked)
					var ScrollOffset = JumpFromTime.Value.Time - ScrollTimeLeft.Time;
					var PrevTimeLeft = PrevItemTime.Time - ScrollOffset;
					ScrollTimeLeft = new TimeUnit(PrevTimeLeft);
				}
				catch(System.Exception)
				{
					//	probably no more data
					EditorApplication.Beep();
					//Debug.LogException(e);
				}
				finally
				{
					Input.MouseJumpPrevPos = null;
				}
			}


			if (Input.MouseJumpNextPos.HasValue)
			{
				try
				{
					int? StreamIndex;
					var JumpFromTime = PositionToTime(Canvas, Input.MouseJumpNextPos.Value, Streams, out StreamIndex);
					var NextItemTime = new TimeUnit(JumpFromTime.Value.Time + 1);
					var NextItem = Bridge.GetNearestOrNextStreamData(Streams[StreamIndex.Value], ref NextItemTime);

					//	want the item to appear under mouse (where we clicked)
					var ScrollOffset = JumpFromTime.Value.Time - ScrollTimeLeft.Time;
					var NextTimeLeft = NextItemTime.Time - ScrollOffset;
					ScrollTimeLeft = new TimeUnit(NextTimeLeft);
				}
				catch (System.Exception)
				{
					//	probably no more data
					EditorApplication.Beep();
					//Debug.LogException(e);
				}
				finally
				{
					Input.MouseJumpNextPos = null;
				}
			}


			Input.ProcessMenuClick(CanvasPositionToStreamAndTime, (StreamAndTime) =>
			{
				if (StreamAndTime.Stream.OnCreateContextMenu == null )
				{
					EditorApplication.Beep();
					return;
				}

				//	create the menu and add items to it
				var Menu = new GenericMenu();

				EnumCommand AppendMenuItem = (Label,Lambda) =>
				{
					if ( string.IsNullOrEmpty(Label) || Label.EndsWith("-") )
					{
						//	todo; use path
						Menu.AddSeparator(null);
					}
					else if ( Lambda == null )
					{
						Menu.AddDisabledItem( new GUIContent(Label) );
					}
					else
					{
						//	argh damned delegates
						GenericMenu.MenuFunction Callback = () => { Lambda(); };
						Menu.AddItem( new GUIContent(Label), false, Callback);
					}
				};

				StreamAndTime.Stream.OnCreateContextMenu(StreamAndTime.Time,AppendMenuItem);
				Menu.ShowAsContext();
			});

		}


		void OnGUI()
		{
			OnInteractionEvent (null);

			if ( Error != null )
			{
				EditorGUILayout.HelpBox (Error.Message, MessageType.Error);
			}

			if (Bridge == null) {
				EditorGUILayout.HelpBox ("No bridge.", MessageType.Warning);
				return;
			}

			try
			{
				TimeUnit LeftTime, RightTime;
				Bridge.GetTimeRange(out LeftTime, out RightTime);

				StickyScroll = GUILayout.Toggle(StickyScroll, "Sticky Scroll", GUILayout.ExpandWidth(true));
				var NewStickySelect = GUILayout.Toggle(StickySelect, "Sticky Select", GUILayout.ExpandWidth(true));
				if (NewStickySelect)
				{
					//	detect change
					var NewTime = (LastStickySelectTime.HasValue && LastStickySelectTime.Value.Time != RightTime.Time);
					LastStickySelectTime = RightTime;
					if (NewTime)
						OnSelectTime(LastStickySelectTime.Value);
				}
				else
				{
					LastStickySelectTime = null;
				}

				if (StickyScroll)
					ScrollTimeLeft = new TimeUnit(RightTime.Time - ScrollVisibleTimeRange.Time);

				var LeftTimef = TimeUnitToFloat(LeftTime);
				var RightTimef = TimeUnitToFloat(RightTime);
				var ScrollTimef = TimeUnitToFloat(ScrollTimeLeft);
				var ScrollSizef = TimeUnitToFloat(ScrollVisibleTimeRange);

				EditorGUI.BeginChangeCheck();
				ScrollTimeLeft = FloatToTimeUnit(GUILayout.HorizontalScrollbar(ScrollTimef, ScrollSizef, LeftTimef, RightTimef, GUILayout.ExpandWidth(true)));
				if (EditorGUI.EndChangeCheck())
				{
					//	user scrolled, turn on sticky mode
					StickyScroll = (ScrollTimeLeft.Time + ScrollVisibleTimeRange.Time) >= RightTime.Time;
				}


				var DrawLeft = ScrollTimeLeft;
				var DrawRight = new TimeUnit(ScrollTimeLeft.Time + FloatToTimeUnit(ScrollSizef).Time);

				Pop.AllocIfNull(ref View);
				var CanvasOptions = new GUILayoutOption[] { GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true) };
				var Border = 2;
				var Canvas = EditorGUILayout.BeginVertical(CanvasOptions);
				Canvas.min = Canvas.min + new Vector2(Border, Border);
				Canvas.max = Canvas.max - new Vector2(Border, Border);

				var Streams = Bridge.Streams;

				HandlePendingEvents(Canvas, Streams);

				if (Event.current.type == EventType.Repaint)
					View.Draw(Canvas, DrawLeft, DrawRight, SelectedTime, HoverTime, Bridge, Streams, CurrentDrag);

				EditorGUILayout.EndVertical();
			}
			catch(System.Exception e)
			{
				EditorGUILayout.HelpBox(e.Message, MessageType.Error);
			}
		}
	}
}

