// Copyright 1998-2015 Epic Games, Inc. All Rights Reserved.

#pragma once

#include "FoliageType_InstancedStaticMesh.h"
#include "FoliageTypeObject.generated.h"

/** A wrapper struct used to allow the use of either FoliageType assets or FoliageType blueprint classes */
USTRUCT()
struct FFoliageTypeObject
{
	GENERATED_USTRUCT_BODY()

	FFoliageTypeObject()
		: FoliageTypeObject(nullptr), TypeInstance(nullptr) {}

	/** Refreshes the type instance based on the assigned object. Intended to be called after some change is made. */
	void RefreshInstance();

	/** Gets the instance of this foliage type. Creates the instance if needed. */
	const UFoliageType_InstancedStaticMesh* GetInstance();

	/** Gets the instance of this foliage type. */
	const UFoliageType_InstancedStaticMesh* GetInstance() const;

	/** @return Whether this would return a valid instance */
	bool IsValid() const;

	bool IsDirty() const;
	void SetClean();

	void PostSerialize(const FArchive& Ar);

private:
	/** The foliage type that will be spawned by the procedural foliage simulation */
	UPROPERTY(Category = ProceduralFoliageSimulation, EditAnywhere, meta=(AllowedClasses="FoliageType_InstancedStaticMesh,Blueprint", DisplayThumbnail="true", ThumbnailSize="X=40 Y=40"))
	UObject* FoliageTypeObject;

	/** The actual instance of the foliage type that is used for spawning */
	UPROPERTY(transient)
	UFoliageType_InstancedStaticMesh* TypeInstance;

	UPROPERTY()
	TSubclassOf<UFoliageType_InstancedStaticMesh> Type_DEPRECATED;
};

template<>
struct TStructOpsTypeTraits<FFoliageTypeObject> : public TStructOpsTypeTraitsBase
{
	enum
	{
		WithPostSerialize = true,
	};
};
