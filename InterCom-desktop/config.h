#pragma once

#ifndef CONFIG_H
#define CONFIG_H

#include <QString>
#include <QCoreApplication>

namespace Config {


const QString CHATS_DIR_PATH = QCoreApplication::applicationDirPath() +
                               "other_data/saved_messages/saved-channels";

const QString ORGANIZATION_NAME = "MyCompany";
const QString APPLICATION_NAME = "Intercom-desktop";
}

#endif 
