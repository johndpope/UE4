// Copyright 1998-2015 Epic Games, Inc. All Rights Reserved.

#pragma once
#include "FoliagePrivate.h"
#include "FoliageTypeObject.h"

void FFoliageTypeObject::RefreshInstance()
{
	//@todo: should verify that the instance is dirty before updating

	// If the foliage type is an asset, use that
	if (auto FoliageTypeAsAsset = Cast<UFoliageType_InstancedStaticMesh>(FoliageTypeObject))
	{
		TypeInstance = FoliageTypeAsAsset;
	}
	else if (auto FoliageTypeAsBP = Cast<UBlueprint>(FoliageTypeObject))
	{
		// If the foliage type is a BP class, create and store an instance of that class
		if (FoliageTypeAsBP->ParentClass->IsChildOf<UFoliageType_InstancedStaticMesh>())
		{
			TypeInstance = NewObject<UFoliageType_InstancedStaticMesh>(GetTransientPackage(), *FoliageTypeAsBP->GeneratedClass);
		}
	}
}

const UFoliageType_InstancedStaticMesh* FFoliageTypeObject::GetInstance()
{
	if (!TypeInstance)
	{
		// If the type instance doesn't exist, try to create it
		RefreshInstance();
	}

	return TypeInstance;
}

const UFoliageType_InstancedStaticMesh* FFoliageTypeObject::GetInstance() const
{
	return TypeInstance;
}

bool FFoliageTypeObject::IsValid() const
{
	return TypeInstance != nullptr;
}

bool FFoliageTypeObject::IsDirty() const
{
	return (!TypeInstance || TypeInstance->ChangeCount != TypeInstance->GetClass()->GetDefaultObject<UFoliageType_InstancedStaticMesh>()->ChangeCount);
}

void FFoliageTypeObject::SetClean()
{
	if (TypeInstance)
	{
		TypeInstance->ChangeCount = TypeInstance->GetClass()->GetDefaultObject<UFoliageType_InstancedStaticMesh>()->ChangeCount;
	}
}

void FFoliageTypeObject::PostSerialize(const FArchive& Ar)
{
	if (Ar.UE4Ver() < VER_UE4_FOLIAGE_WITH_ASSET_OR_CLASS)
	{
		// Set the type object to be the previously set FoliageType BP class
		FoliageTypeObject = Type_DEPRECATED->ClassGeneratedBy;
	}
}
