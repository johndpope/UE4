// Copyright 1998-2014 Epic Games, Inc. All Rights Reserved.

#pragma once

#include "WidgetTree.generated.h"

/** The widget tree manages the collection of widgets in a blueprint widget. */
UCLASS()
class UMG_API UWidgetTree : public UObject
{
	GENERATED_UCLASS_BODY()

public:
	/** The root widget of the tree */
	UPROPERTY()
	UWidget* RootWidget;

public:

	UWidget* FindWidget(const FString& Name) const;

	bool RemoveWidget(UWidget* Widget);

	class UPanelWidget* FindWidgetParent(UWidget* Widget, int32& OutChildIndex);

	void GetAllWidgets(TArray<UWidget*>& Widgets) const;

	void GetChildWidgets(UWidget* Parent, TArray<UWidget*>& Widgets) const;

	/** Constructs the widget, and adds it to the tree. */
	template< class T >
	T* ConstructWidget(TSubclassOf<UWidget> WidgetType)
	{
		// TODO UMG Editor only?
		Modify();

		// TODO UMG Don't have the widget tree responsible for construction and adding to the tree.

		UWidget* Widget = (UWidget*)ConstructObject<UWidget>(WidgetType, this);
		Widget->SetFlags(RF_Transactional);

		return (T*)Widget;
	}
};