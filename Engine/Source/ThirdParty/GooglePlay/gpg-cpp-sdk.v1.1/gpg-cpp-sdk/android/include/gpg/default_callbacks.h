/**
 * @file gpg/default_callbacks.h
 * @copyright Copyright 2014 Google Inc. All Rights Reserved.
 * @brief Default implementation of callbacks used by GameServices.
 */

#ifndef GPG_DEFAULT_CALLBACKS_H_
#define GPG_DEFAULT_CALLBACKS_H_

#ifndef __cplusplus
#error Header file supports C++ only
#endif  // __cplusplus

#include <functional>
#include <string>
#include "gpg/common.h"
#include "gpg/multiplayer_invitation.h"
#include "gpg/player.h"
#include "gpg/quest.h"
#include "gpg/status.h"
#include "gpg/turn_based_match.h"
#include "gpg/types.h"

namespace gpg {
enum class LogLevel;  // Defined in gpg/types.h

/**
 * This is the default value for Builder::SetLogging. By default, logs are
 * written in a platform-specific manner (i.e., to the Android log or NSLog).
 */
void DEFAULT_ON_LOG(LogLevel level, std::string const &message) GPG_EXPORT;

/**
 * This is the default value for Builder:SetOnAuthActionStarted.
 * By default, this function logs the name of the auth operation that has
 * started.
 */
void DEFAULT_ON_AUTH_ACTION_STARTED(AuthOperation op) GPG_EXPORT;

/**
 * This is the default value for Builder::SetOnAuthActionFinished.
 * By default, this function logs the name of the auth operation that has
 * finished, as well as the completion status of the operation.
 */
void DEFAULT_ON_AUTH_ACTION_FINISHED(AuthOperation op,
                                     AuthStatus status) GPG_EXPORT;
/**
 * This is the default value of Builder::SetOnMultiplayerInvitationEvent.
 * By default, this function logs the id of the invitation.
 */
void DEFAULT_ON_MULTIPLAYER_INVITATION_RECEIVED(
    TurnBasedMultiplayerEvent event,
    std::string invitation_id,
    MultiplayerInvitation invitation) GPG_EXPORT;

/**
 * This is the default value of Builder::SetOnTurnBasedMatchEvent.
 * By default, this function logs the id of the turn based match which was
 * updated.
 */
void DEFAULT_ON_TURN_BASED_MULTIPLAYER_EVENT(
    TurnBasedMultiplayerEvent event,
    std::string match_id,
    TurnBasedMatch match) GPG_EXPORT;

/**
 * This is the default value of Builder::SetOnQuestCompleted.
 * By default, this function logs the id of the quest which was completed.
 */
void DEFAULT_ON_QUEST_COMPLETED(Quest quest) GPG_EXPORT;
}  // namespace gpg

#endif  // GPG_DEFAULT_CALLBACKS_H_
