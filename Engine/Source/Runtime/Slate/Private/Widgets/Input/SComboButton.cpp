// Copyright 1998-2015 Epic Games, Inc. All Rights Reserved.

#include "SlatePrivatePCH.h"


void SComboButton::Construct( const FArguments& InArgs )
{
	check(InArgs._ComboButtonStyle);

	// Work out which values we should use based on whether we were given an override, or should use the style's version
	const FButtonStyle* const OurButtonStyle = InArgs._ButtonStyle ? InArgs._ButtonStyle : &InArgs._ComboButtonStyle->ButtonStyle;

	MenuBorderBrush = &InArgs._ComboButtonStyle->MenuBorderBrush;
	MenuBorderPadding = InArgs._ComboButtonStyle->MenuBorderPadding;
	
	OnComboBoxOpened = InArgs._OnComboBoxOpened;
	ContentWidgetPtr = InArgs._MenuContent.Widget;
	bIsFocusable = InArgs._IsFocusable;

	TSharedPtr<SHorizontalBox> HBox;

	SMenuAnchor::Construct( SMenuAnchor::FArguments()
		.Placement(InArgs._MenuPlacement)
		.Method(InArgs._Method)
		.OnMenuOpenChanged(InArgs._OnMenuOpenChanged)
		.OnGetMenuContent(InArgs._OnGetMenuContent)
		[
			SNew( SButton )
			.ButtonStyle( OurButtonStyle )
			.ClickMethod( EButtonClickMethod::MouseDown )
			.OnClicked( this, &SComboButton::OnButtonClicked )
			.ContentPadding( InArgs._ContentPadding )
			.ForegroundColor( InArgs._ForegroundColor )
			.ButtonColorAndOpacity( InArgs._ButtonColorAndOpacity )
			.IsFocusable( InArgs._IsFocusable )
			[
				// Button and down arrow on the right
				// +-------------------+---+
				// | Button Content    | v |
				// +-------------------+---+
				SAssignNew( HBox, SHorizontalBox )
				+ SHorizontalBox::Slot()
				.Expose( ButtonContentSlot )
				.FillWidth( 1 )
				.HAlign( InArgs._HAlign )
				.VAlign( InArgs._VAlign )
				[
					InArgs._ButtonContent.Widget
				]
				+ SHorizontalBox::Slot()
				.AutoWidth()
				.HAlign( HAlign_Center )
				.VAlign( VAlign_Center )
				.Padding( InArgs._HasDownArrow ? 2 : 0 )
				[
					SNew( SImage )
					.Visibility( InArgs._HasDownArrow ? EVisibility::Visible : EVisibility::Collapsed )
					.Image( &InArgs._ComboButtonStyle->DownArrowImage )
					// Inherit tinting from parent
					. ColorAndOpacity( FSlateColor::UseForeground() )
				]
			]
		]
	);

	
	// The menu that pops up when we press the button.
	// We keep this content around, and then put it into a new window when we need to pop
	// it up.
	SetMenuContent( InArgs._MenuContent.Widget );
}

FReply SComboButton::OnButtonClicked()
{
	// Button was clicked; show the popup.
	// Do nothing if clicking on the button also dismissed the menu, because we will end up doing the same thing twice.
	this->SetIsOpen( ShouldOpenDueToClick(), bIsFocusable );

	// If the menu is open, execute the related delegate.
	if( IsOpen() && OnComboBoxOpened.IsBound() )
	{
		OnComboBoxOpened.Execute();
	}

	// Focusing any newly-created widgets must occur after they have been added to the UI root.
	FReply ButtonClickedReply = FReply::Handled();
	
	if (bIsFocusable)
	{
		TSharedPtr<SWidget> WidgetToFocus = WidgetToFocusPtr.Pin();
		if (!WidgetToFocus.IsValid())
		{
			// no explicitly focused widget, try to focus the content
			WidgetToFocus = MenuContent;
		}

		if (!WidgetToFocus.IsValid())
		{
			// no content, so try to focus the original widget set on construction
			WidgetToFocus = ContentWidgetPtr.Pin();
		}

		if (WidgetToFocus.IsValid())
		{
			ButtonClickedReply.SetUserFocus(WidgetToFocus.ToSharedRef(), EFocusCause::SetDirectly);
		}
	}

	return ButtonClickedReply;
}

void SComboButton::SetMenuContent(TSharedRef<SWidget> InContent)
{
	MenuContent = 
		SNew(SBorder)
		.BorderImage(MenuBorderBrush)
		.Padding(MenuBorderPadding)
		[
			InContent
		];
}

void SComboButton::SetOnGetMenuContent(FOnGetContent InOnGetMenuContent)
{
	OnGetMenuContent = InOnGetMenuContent;
}
